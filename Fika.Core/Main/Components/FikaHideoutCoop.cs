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
using Fika.Core.Networking;
using Fika.Core.Networking.Http;
using Fika.Core.Networking.Models.Hideout;
using Fika.Core.Networking.Packets.Generic;
using Fika.Core.Networking.Packets.Generic.SubPackets;
using static Fika.Core.Networking.NetworkUtils;

namespace Fika.Core.Main.Components;

/// <summary>
/// 在 <see cref="HideoutGame"/> 内接 Fika 联机：主人 Host、客人 Join，复用 ObservedPlayer 与 PlayerState 包。
/// 不替换 HideoutGame，也不设置 <see cref="FikaBackendUtils.RequestFikaWorld"/>。
/// </summary>
public static class FikaHideoutCoop
{
    public static bool IsActive { get; private set; }

    private static readonly ManualLogSource _logger = Logger.CreateLogSource("Fika.HideoutCoop");
    private const float JoinRetrySeconds = 2f;
    private const float HostHeartbeatSeconds = 15f;

    private static bool _busy;
    private static bool _stopRequested;
    private static bool _isGuest;
    private static string _ownerAccountId = "";
    private static bool _characterSent;
    private static HideoutPacketSender _sender;
    private static float _nextJoinAttempt = -999f;
    private static float _nextHostHeartbeat = -999f;
    private static bool _subscribed;

    public static void Tick(bool inHideout, bool isGuest, string ownerAccountId)
    {
        if (Singleton<IFikaGame>.Instantiated)
        {
            return;
        }

        if (!inHideout || string.IsNullOrEmpty(ownerAccountId))
        {
            Stop();
            return;
        }

        if (IsActive && (_isGuest != isGuest || !string.Equals(_ownerAccountId, ownerAccountId, StringComparison.Ordinal)))
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

            if (isGuest)
            {
                if (Time.unscaledTime < _nextJoinAttempt)
                {
                    return;
                }

                _nextJoinAttempt = Time.unscaledTime + JoinRetrySeconds;
                _ = StartClientAsync(ownerAccountId);
                return;
            }

            _ = StartHostAsync(ownerAccountId);
            return;
        }

        EnsureLocalSync();
        if (!_isGuest && Time.unscaledTime >= _nextHostHeartbeat)
        {
            RegisterHost();
        }
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
        _nextJoinAttempt = -999f;
        _nextHostHeartbeat = -999f;
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
            FikaHideoutHostResponse host;
            try
            {
                host = FikaRequestHandler.GetHideoutHost(new FikaHideoutHostRequest { AccountId = ownerAccountId });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"GetHideoutHost failed: {ex.Message}");
                return;
            }

            if (host == null || !host.Ok)
            {
                return;
            }

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

    private static void EnsureLocalSync()
    {
        var player = LocalHideoutPlayer();
        var manager = Singleton<IFikaNetworkManager>.Instance;
        if (player == null || manager == null || manager.NetId <= 0)
        {
            return;
        }

        if (_sender == null)
        {
            _sender = HideoutPacketSender.Create(player, (ushort)manager.NetId, manager);
        }

        if (!_characterSent)
        {
            SendLocalCharacter(null);
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
            packet.PlayerInfoPacket.ControllerType = HandsControllerTypeConvert.FromController(player.HandsController);
            if (player.HandsController.Item != null)
            {
                packet.PlayerInfoPacket.ItemId = player.HandsController.Item.Id;
            }

            packet.PlayerInfoPacket.IsStationary = player.MovementContext.IsStationaryWeaponInHands;
        }

        if (peer != null && Singleton<FikaServer>.Instantiated)
        {
            Singleton<FikaServer>.Instance.SendGenericPacketToPeer(EGenericSubPacketType.SendCharacter, packet, peer);
            return;
        }

        manager.SendGenericPacket(EGenericSubPacketType.SendCharacter, packet, true);
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
                Ips = CollectHostIps(),
                Port = FikaPlugin.Instance.Settings.UDPPort.Value,
                ServerGuid = FikaBackendUtils.ServerGuid.ToString(),
                NatPunch = FikaPlugin.Instance.Settings.UseNATPunching.Value,
                UseFikaNatPunchServer = FikaPlugin.Instance.Settings.UseFikaNATPunchServer.Value
            };
            FikaRequestHandler.SetHideoutHost(request);
            _nextHostHeartbeat = Time.unscaledTime + HostHeartbeatSeconds;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"SetHideoutHost failed: {ex.Message}");
        }
    }

    private static string[] CollectHostIps()
    {
        var externalIp = FikaPlugin.Instance.Settings.ForceIP.Value;
        if (string.IsNullOrEmpty(externalIp))
        {
            externalIp = FikaPlugin.Instance.WanIP != null ? FikaPlugin.Instance.WanIP.ToString() : "";
        }

        List<string> ipAddresses = [externalIp];
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
