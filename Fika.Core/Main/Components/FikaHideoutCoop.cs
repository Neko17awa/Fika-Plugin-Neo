using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using Fika.Core.Main.GameMode;
using Fika.Core.Main.PacketHandlers;
using Fika.Core.Main.Utils;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using LiteNetLib;
using Fika.Core.Networking;
using Fika.Core.Networking.Http;
using Fika.Core.Networking.Models.Hideout;
using Fika.Core.Networking.Packets;
using Fika.Core.Networking.Packets.Generic;
using Fika.Core.Networking.Packets.Generic.SubPackets;
using Fika.Core.Networking.Packets.World;
using static Fika.Core.Networking.NetworkUtils;

namespace Fika.Core.Main.Components;

/// <summary>
/// 在 <see cref="HideoutGame"/> 内接 Fika 联机：主人 Host、客人 Join，复用 ObservedPlayer 与 PlayerState 包。
/// 不替换 HideoutGame，也不设置 <see cref="FikaBackendUtils.RequestFikaWorld"/>。
/// </summary>
public static class FikaHideoutCoop
{
    public static bool IsActive { get; private set; }

    public static bool IsHosting => IsActive && !_isGuest;

    public static bool IsGuest => IsActive && _isGuest;

    /// <summary>
    /// 已锁定参观身份（含联机尚未起来时），客人不得改灯光/发电机/装饰档案。
    /// </summary>
    public static bool IsVisitGuest => _visitLocked || _isGuest;

    /// <summary>
    /// 参观中主人的显示名（昵称），给建造 UI 打拥有者标识。
    /// </summary>
    public static string VisitOwnerName => _ownerDisplayName ?? "";

    public static bool TryGetHostPublish(out FikaHideoutHostRequest request)
    {
        request = _lastHostRequest;
        return IsHosting && request != null && !string.IsNullOrEmpty(request.AccountId);
    }

    private static readonly ManualLogSource _logger = Logger.CreateLogSource("Fika.HideoutCoop");
    private const float JoinRetrySeconds = 2f;
    private const float HostHeartbeatSeconds = 15f;
    private const float HostCharacterRetrySeconds = 4f;

    private static bool _busy;
    private static bool _stopRequested;
    private static bool _isGuest;
    private static string _ownerAccountId = "";
    private static bool _characterSent;
    private static HideoutPacketSender _sender;
    private static float _nextJoinAttempt = -999f;
    private static float _nextHostHeartbeat = -999f;
    private static bool _subscribed;
    private static bool _visitLocked;
    private static bool _ownConfirmed;
    private static string _lockedOwnerId = "";
    private static string _ownerDisplayName = "";
    private static FikaHideoutHostRequest _lastHostRequest;
    private static float _nextHostCharacterSend = -999f;

    /// <summary>
    /// HideoutSelectedHandler 一进来就定角色：客人藏身处绝不开 Host。
    /// </summary>
    public static void OnHideoutSelected(HideoutData hideoutData)
    {
        if (hideoutData == null)
        {
            return;
        }

        if (hideoutData.IsGuest)
        {
            _visitLocked = true;
            _ownConfirmed = false;
            if (string.IsNullOrEmpty(FikaBackendUtils.HideoutVisitInProgressId)
                && !string.IsNullOrEmpty(hideoutData.OwnerAccountId))
            {
                FikaBackendUtils.HideoutVisitInProgressId = hideoutData.OwnerAccountId;
            }

            _lockedOwnerId = FirstNonEmpty(
                FikaBackendUtils.HideoutVisitInProgressId,
                hideoutData.OwnerAccountId);
            if (!string.IsNullOrEmpty(hideoutData.OwnerName))
            {
                _ownerDisplayName = hideoutData.OwnerName;
            }

            _logger.LogInfo($"Hideout selected as guest owner={_lockedOwnerId} name={_ownerDisplayName}");
            return;
        }

        _visitLocked = false;
        _ownConfirmed = true;
        _lockedOwnerId = "";
        _ownerDisplayName = "";
        FikaBackendUtils.HideoutVisitInProgressId = string.Empty;
        _logger.LogInfo("Hideout selected as owner");
    }

    public static void OnSetGuest(bool isGuest, string ownerAccountId)
    {
        if (isGuest)
        {
            _visitLocked = true;
            _ownConfirmed = false;
            if (!string.IsNullOrEmpty(ownerAccountId)
                && string.IsNullOrEmpty(FikaBackendUtils.HideoutVisitInProgressId))
            {
                FikaBackendUtils.HideoutVisitInProgressId = ownerAccountId;
            }

            _lockedOwnerId = FirstNonEmpty(
                FikaBackendUtils.HideoutVisitInProgressId,
                ownerAccountId,
                _lockedOwnerId);
            _logger.LogInfo($"SetGuest(true) owner={_lockedOwnerId}");
            return;
        }

        if (_visitLocked || !string.IsNullOrEmpty(FikaBackendUtils.HideoutVisitInProgressId))
        {
            _logger.LogWarning("SetGuest(false) ignored while visiting another hideout");
            return;
        }

        _ownConfirmed = true;
        _visitLocked = false;
        _lockedOwnerId = "";
        _ownerDisplayName = "";
        FikaBackendUtils.HideoutVisitInProgressId = string.Empty;
        _logger.LogInfo("SetGuest(false) own hideout confirmed");
    }

    public static void OnHideoutUnloaded()
    {
        _visitLocked = false;
        _ownConfirmed = false;
        _lockedOwnerId = "";
        _ownerDisplayName = "";
        FikaBackendUtils.HideoutVisitInProgressId = string.Empty;
        Stop();
    }

    public static void Tick(bool inHideout, bool isGuest, string ownerAccountId)
    {
        if (Singleton<IFikaGame>.Instantiated)
        {
            return;
        }

        if (!inHideout)
        {
            Stop();
            return;
        }

        if (!TryResolveRole(isGuest, ownerAccountId, out var guest, out var ownerId))
        {
            return;
        }

        if (IsActive && (_isGuest != guest || !string.Equals(_ownerAccountId, ownerId, StringComparison.Ordinal)))
        {
            Stop();
            return;
        }

        if (_busy)
        {
            return;
        }

        if (!IsActive)
        {
            if (RaidNetManagerRunning())
            {
                return;
            }

            if (guest)
            {
                if (Time.unscaledTime < _nextJoinAttempt)
                {
                    return;
                }

                _nextJoinAttempt = Time.unscaledTime + JoinRetrySeconds;
                _ = StartClientAsync(ownerId);
                return;
            }

            _ = StartHostAsync(ownerId);
            return;
        }

        EnsureLocalSync();
        HideoutItemSync.Tick();
        if (_isGuest)
        {
            MaybeRequestHostCharacter();
            return;
        }

        HideoutWorldSync.Tick();
        if (Time.unscaledTime >= _nextHostHeartbeat)
        {
            RegisterHost();
        }
    }

    /// <summary>
    /// 角色只认 HideoutSelectedHandler / SetGuest。IsGuest 默认 false，绝不能据此开 Host。
    /// </summary>
    private static bool TryResolveRole(bool isGuestHint, string ownerAccountIdHint, out bool isGuest, out string ownerAccountId)
    {
        isGuest = false;
        ownerAccountId = "";

        var visitId = FikaBackendUtils.HideoutVisitInProgressId;
        var hideoutOwner = HideoutPlayerOwner();

        if (_visitLocked || !string.IsNullOrEmpty(visitId))
        {
            isGuest = true;
            ownerAccountId = FirstNonEmpty(
                visitId,
                _lockedOwnerId,
                hideoutOwner != null ? hideoutOwner.HideoutOwnerAccountId : "",
                isGuestHint ? ownerAccountIdHint : "");
            return !string.IsNullOrEmpty(ownerAccountId);
        }

        if (hideoutOwner != null && hideoutOwner.IsGuest)
        {
            isGuest = true;
            ownerAccountId = FirstNonEmpty(ownerAccountIdHint, hideoutOwner.HideoutOwnerAccountId);
            return !string.IsNullOrEmpty(ownerAccountId);
        }

        if (isGuestHint && !string.IsNullOrEmpty(ownerAccountIdHint) && ownerAccountIdHint != LocalAccountId())
        {
            isGuest = true;
            ownerAccountId = ownerAccountIdHint;
            return true;
        }

        if (!_ownConfirmed)
        {
            return false;
        }

        isGuest = false;
        ownerAccountId = LocalAccountId();
        return !string.IsNullOrEmpty(ownerAccountId);
    }

    private static HideoutPlayerOwner HideoutPlayerOwner()
    {
        var app = Singleton<ClientApplication<IEftSession>>.Instantiated
            ? Singleton<ClientApplication<IEftSession>>.Instance as TarkovApplication
            : null;
        return app != null && app.HideoutControllerAccess != null
            ? app.HideoutControllerAccess._playerOwner
            : null;
    }

    private static string LocalAccountId()
    {
        try
        {
            var app = Singleton<ClientApplication<IEftSession>>.Instantiated
                ? Singleton<ClientApplication<IEftSession>>.Instance as TarkovApplication
                : null;
            return app?.Session?.Profile?.AccountId ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string FirstNonEmpty(params string[] values)
    {
        if (values == null)
        {
            return "";
        }

        for (var i = 0; i < values.Length; i++)
        {
            if (!string.IsNullOrEmpty(values[i]))
            {
                return values[i];
            }
        }

        return "";
    }

    public static void Stop()
    {
        if (_busy)
        {
            _stopRequested = true;
            return;
        }

        if (!IsActive && _sender == null && !_subscribed)
        {
            _stopRequested = false;
            HideoutWorldSync.Reset();
            HideoutItemSync.Reset();
            return;
        }

        if (Singleton<IFikaGame>.Instantiated)
        {
            return;
        }

        try
        {
            if (!_isGuest && !string.IsNullOrEmpty(_ownerAccountId))
            {
                FikaRequestHandler.LeaveHideoutHost(new FikaHideoutHostRequest { AccountId = _ownerAccountId });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"LeaveHideoutHost failed: {ex.Message}");
        }

        UnsubscribePeerConnected();
        DestroySender();

        var wasHideout = FikaBackendUtils.IsHideoutSession;
        FikaBackendUtils.IsHideoutSession = false;

        if (wasHideout)
        {
            try
            {
                if (Singleton<FikaServer>.Instantiated)
                {
                    NetManagerUtils.DestroyNetManager(true);
                }
                else if (Singleton<FikaClient>.Instantiated)
                {
                    NetManagerUtils.DestroyNetManager(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"DestroyNetManager failed: {ex.Message}");
            }
        }

        IsActive = false;
        _busy = false;
        _stopRequested = false;
        _isGuest = false;
        _ownerAccountId = "";
        _characterSent = false;
        _lastHostRequest = null;
        _nextHostCharacterSend = -999f;
        _nextJoinAttempt = -999f;
        _nextHostHeartbeat = -999f;
        HideoutWorldSync.Reset();
        HideoutItemSync.Reset();
        HideoutCoopBuild.Reset();
        _logger.LogInfo("Hideout coop stopped");
    }

    private static bool RaidNetManagerRunning()
    {
        if (FikaBackendUtils.IsHideoutSession)
        {
            return false;
        }

        return Singleton<FikaServer>.Instantiated || Singleton<FikaClient>.Instantiated;
    }

    private static async Task StartHostAsync(string ownerAccountId)
    {
        if (_visitLocked || !string.IsNullOrEmpty(FikaBackendUtils.HideoutVisitInProgressId))
        {
            _logger.LogWarning("StartHost ignored: visiting another hideout");
            return;
        }

        if (!_ownConfirmed)
        {
            _logger.LogWarning("StartHost ignored: own hideout not confirmed");
            return;
        }

        if (Singleton<FikaServer>.Instantiated)
        {
            _logger.LogWarning("StartHost ignored: FikaServer already running");
            _ownerAccountId = ownerAccountId;
            _isGuest = false;
            IsActive = true;
            BindHideoutHostNetId();
            SubscribePeerConnected();
            RegisterHost();
            return;
        }

        _busy = true;
        try
        {
            FikaBackendUtils.IsHideoutSession = true;
            FikaBackendUtils.IsScav = false;
            FikaBackendUtils.GroupId = HideoutGroupId(ownerAccountId);
            FikaBackendUtils.ClientType = EClientType.Host;
            FikaBackendUtils.ServerGuid = Guid.NewGuid();

            NetManagerUtils.CreateNetManager(true);
            await NetManagerUtils.InitNetManager(true);
            BindHideoutHostNetId();
            NetManagerUtils.DisableLoadingScreenUI();

            var coop = Singleton<IFikaNetworkManager>.Instance.CoopHandler;
            if (coop != null)
            {
                coop.ShouldSync = true;
            }

            _ownerAccountId = ownerAccountId;
            _isGuest = false;
            IsActive = true;
            SubscribePeerConnected();
            RegisterHost();
            _logger.LogInfo($"Hideout host started for {ownerAccountId}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"StartHost failed: {ex.Message}");
            FikaBackendUtils.IsHideoutSession = false;
            FikaBackendUtils.ClientType = EClientType.None;
            try
            {
                if (Singleton<FikaServer>.Instantiated)
                {
                    NetManagerUtils.DestroyNetManager(true);
                }
            }
            catch
            {
                // ignored
            }
        }
        finally
        {
            _busy = false;
            if (_stopRequested)
            {
                Stop();
            }
        }
    }

    private static async Task StartClientAsync(string ownerAccountId)
    {
        _busy = true;
        try
        {
            _logger.LogInfo($"Hideout client joining owner {ownerAccountId}");
            if (Singleton<FikaServer>.Instantiated)
            {
                _logger.LogWarning("Destroying leftover FikaServer before hideout join");
                try
                {
                    NetManagerUtils.DestroyNetManager(true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Destroy leftover FikaServer failed: {ex.Message}");
                }
            }
            FikaHideoutHostResponse host = FikaHideoutExt.PeekRemoteHost();
            if (HostUsable(host))
            {
                _logger.LogInfo($"Using offered hideout host port={host.Port}");
            }
            else
            {
                try
                {
                    host = await Task.Run(() =>
                        FikaRequestHandler.GetHideoutHost(new FikaHideoutHostRequest { AccountId = ownerAccountId }));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"GetHideoutHost({ownerAccountId}) failed: {ex.Message}");
                    host = null;
                }
            }

            if (!HostUsable(host))
            {
                _logger.LogInfo($"Hideout host for {ownerAccountId} is not published yet");
                return;
            }

            _logger.LogInfo($"Hideout host found for {ownerAccountId} port={host.Port}");

            using var pingingClient = await NetManagerUtils.CreatePingingClient();
            if (!pingingClient.InitFromHost(host.ToGetHostResponse()))
            {
                _logger.LogWarning("Hideout ping init failed");
                return;
            }

            var knock = FikaBackendUtils.ServerGuid.ToString();
            var reachable = await pingingClient.AttemptToPingHost(knock, false);
            if (!reachable)
            {
                _logger.LogWarning("Hideout host not reachable");
                return;
            }

            FikaBackendUtils.IsHideoutSession = true;
            FikaBackendUtils.IsScav = false;
            FikaBackendUtils.GroupId = HideoutGroupId(ownerAccountId);
            FikaBackendUtils.ClientType = EClientType.Client;

            NetManagerUtils.CreateNetManager(false);
            await NetManagerUtils.InitNetManager(false);
            NetManagerUtils.DisableLoadingScreenUI();

            var coop = Singleton<IFikaNetworkManager>.Instance.CoopHandler;
            if (coop != null)
            {
                coop.ShouldSync = true;
            }

            _ownerAccountId = ownerAccountId;
            _isGuest = true;
            IsActive = true;
            _logger.LogInfo($"Hideout client joined {ownerAccountId}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"StartClient failed: {ex.Message}");
            FikaBackendUtils.IsHideoutSession = false;
            FikaBackendUtils.ClientType = EClientType.None;
            try
            {
                if (Singleton<FikaClient>.Instantiated)
                {
                    NetManagerUtils.DestroyNetManager(false);
                }
            }
            catch
            {
                // ignored
            }
        }
        finally
        {
            _busy = false;
            if (_stopRequested)
            {
                Stop();
            }
        }
    }

    private static bool HostUsable(FikaHideoutHostResponse host)
    {
        if (host == null || !host.Ok || host.Port == 0 || host.Ips == null || host.Ips.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < host.Ips.Length; i++)
        {
            if (!string.IsNullOrEmpty(host.Ips[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureLocalSync()
    {
        var player = LocalHideoutPlayer();
        var manager = Singleton<IFikaNetworkManager>.Instance;
        if (player == null || manager == null || manager.NetId <= 0)
        {
            return;
        }

        if (_sender == null || _sender.NetId != (ushort)manager.NetId)
        {
            DestroySender();
            _sender = HideoutPacketSender.Create(player, (ushort)manager.NetId, manager);
        }

        if (!_characterSent)
        {
            SendLocalCharacter(null);
            HideoutItemSync.SendCurrentState();
            _characterSent = true;
        }
    }

    private static void SubscribePeerConnected()
    {
        if (_subscribed)
        {
            return;
        }

        FikaEventDispatcher.SubscribeEvent<PeerConnectedEvent>(OnPeerConnected);
        _subscribed = true;
    }

    private static void UnsubscribePeerConnected()
    {
        if (!_subscribed)
        {
            return;
        }

        FikaEventDispatcher.UnsubscribeEvent<PeerConnectedEvent>(OnPeerConnected);
        _subscribed = false;
    }

    private static void OnPeerConnected(PeerConnectedEvent evt)
    {
        if (!IsActive || _isGuest)
        {
            return;
        }

        SendLocalCharacter(evt.Peer);
        HideoutWorldSync.SendToPeer(evt.Peer);
        HideoutItemSync.SendToPeer(evt.Peer);
    }

    public static void SendLocalCharacterToPeer(NetPeer peer)
    {
        SendLocalCharacter(peer);
    }

    private static void BindHideoutHostNetId()
    {
        if (!Singleton<FikaServer>.Instantiated)
        {
            return;
        }

        Singleton<FikaServer>.Instance.AssignHideoutHostNetId();
        DestroySender();
        _characterSent = false;
    }

    private static void MaybeRequestHostCharacter()
    {
        var manager = Singleton<IFikaNetworkManager>.Instance;
        if (manager?.ObservedPlayers != null && manager.ObservedPlayers.Count > 0)
        {
            return;
        }

        if (!Singleton<FikaClient>.Instantiated || Time.unscaledTime < _nextHostCharacterSend)
        {
            return;
        }

        _nextHostCharacterSend = Time.unscaledTime + HostCharacterRetrySeconds;
        RequestPacket request = new()
        {
            Type = ERequestSubPacketType.CharacterSync,
            RequestSubPacket = new RequestSubPackets.RequestCharactersPacket([1])
        };
        Singleton<FikaClient>.Instance.SendData(ref request, DeliveryMethod.ReliableOrdered);
    }

    private static void SendLocalCharacter(NetPeer peer)
    {
        var player = LocalHideoutPlayer();
        var manager = Singleton<IFikaNetworkManager>.Instance;
        if (player == null || manager == null || manager.NetId <= 0)
        {
            return;
        }

        var packet = SendCharacterPacket.FromValue(new PlayerInfoPacket
        {
            Profile = player.Profile,
            ControllerId = player.InventoryController != null ? player.InventoryController.CurrentId : MongoID.Generate(true),
            FirstOperationId = player.InventoryController != null ? player.InventoryController.NextOperationId : (ushort)0,
            IsZombie = player.Profile.Info?.Settings != null && player.Profile.Info.Settings.UseSimpleAnimator
        }, player.HealthController != null && player.HealthController.IsAlive, false, player.Transform.position, manager.NetId);

        if (player.ActiveHealthController != null)
        {
            packet.PlayerInfoPacket.HealthByteArray = player.ActiveHealthController.SerializeState();
        }
        else if (player.Profile?.Health != null)
        {
            packet.PlayerInfoPacket.HealthByteArray = player.Profile.Health.SerializeHealthInfo();
        }

        if (player.HandsController != null)
        {
            if (HideoutItemSync.LooksUnarmed(player) || player.HandsController is IEmptyHandsController)
            {
                packet.PlayerInfoPacket.ControllerType = EHandsControllerType.Empty;
            }
            else
            {
                packet.PlayerInfoPacket.ControllerType = HandsControllerTypeConvert.FromController(player.HandsController);
                if (player.HandsController.Item != null && player.HandsController.Item is not EmptyHands)
                {
                    packet.PlayerInfoPacket.ItemId = player.HandsController.Item.Id;
                }

                packet.PlayerInfoPacket.IsStationary = player.MovementContext.IsStationaryWeaponInHands;
            }
        }

        if (peer != null && Singleton<FikaServer>.Instantiated)
        {
            Singleton<FikaServer>.Instance.SendGenericPacketToPeer(EGenericSubPacketType.SendCharacter, packet, peer);
            _logger.LogInfo($"Hideout character sent to peer {peer.Id} netId={manager.NetId}");
            return;
        }

        manager.SendGenericPacket(EGenericSubPacketType.SendCharacter, packet, true);
        _logger.LogInfo($"Hideout character sent netId={manager.NetId}");
    }

    private static void RegisterHost()
    {
        if (string.IsNullOrEmpty(_ownerAccountId))
        {
            return;
        }

        try
        {
            var request = new FikaHideoutHostRequest
            {
                AccountId = _ownerAccountId,
                Aliases = HostAliasIds(_ownerAccountId),
                Ips = CollectHostIps(),
                Port = FikaPlugin.Instance.Settings.UDPPort.Value,
                ServerGuid = FikaBackendUtils.ServerGuid.ToString(),
                NatPunch = FikaPlugin.Instance.Settings.UseNATPunching.Value,
                UseFikaNatPunchServer = FikaPlugin.Instance.Settings.UseFikaNATPunchServer.Value
            };
            FikaRequestHandler.SetHideoutHost(request);
            var firstPublish = _lastHostRequest == null;
            _lastHostRequest = request;
            _nextHostHeartbeat = Time.unscaledTime + HostHeartbeatSeconds;
            if (firstPublish)
            {
                _logger.LogInfo($"SetHideoutHost account={_ownerAccountId} port={request.Port}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"SetHideoutHost failed: {ex.Message}");
        }
    }

    private static string[] HostAliasIds(string ownerAccountId)
    {
        var ids = new List<string>();
        AddUnique(ids, ownerAccountId);
        AddUnique(ids, LocalAccountId());
        AddUnique(ids, LocalProfileId());
        return [.. ids];
    }

    private static void AddUnique(List<string> ids, string value)
    {
        if (!string.IsNullOrEmpty(value) && !ids.Contains(value))
        {
            ids.Add(value);
        }
    }

    private static string LocalProfileId()
    {
        try
        {
            var app = Singleton<ClientApplication<IEftSession>>.Instantiated
                ? Singleton<ClientApplication<IEftSession>>.Instance as TarkovApplication
                : null;
            return app?.Session?.Profile?.Id ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string[] CollectHostIps()
    {
        var externalIp = FikaPlugin.Instance.Settings.ForceIP.Value;
        if (string.IsNullOrEmpty(externalIp))
        {
            externalIp = FikaPlugin.Instance.WanIP != null ? FikaPlugin.Instance.WanIP.ToString() : "";
        }

        List<string> ipAddresses = [];
        if (ValidateIP(externalIp))
        {
            ipAddresses.Add(externalIp);
        }
        var localIps = FikaPlugin.Instance.LocalIPs;
        if (localIps != null)
        {
            for (var i = 0; i < localIps.Length; i++)
            {
                var ip = localIps[i];
                if (ValidateIP(ip) && !ipAddresses.Contains(ip))
                {
                    ipAddresses.Add(ip);
                }
            }
        }

        return [.. ipAddresses];
    }

    private static Player LocalHideoutPlayer()
    {
        if (!Singleton<GameWorld>.Instantiated)
        {
            return null;
        }

        var world = Singleton<GameWorld>.Instance;
        return world is HideoutGameWorld ? world.MainPlayer : null;
    }

    private static void DestroySender()
    {
        if (_sender == null)
        {
            return;
        }

        try
        {
            _sender.DestroyThis();
        }
        catch
        {
            // ignored
        }

        _sender = null;
    }

    private static string HideoutGroupId(string ownerAccountId)
    {
        return "hideout-" + ownerAccountId;
    }
}
