using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.Hideout;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using Fika.Core.Networking;
using Fika.Core.Networking.Packets.Hideout;
using Newtonsoft.Json;

namespace Fika.Core.Main.Components;

/// <summary>
/// 访客从自己的仓库扣材料，主人 SPT 写藏身处档案。
/// </summary>
public static class HideoutCoopBuild
{
    public const string SpendAction = "FikaHideoutCoopSpend";
    public const string ApplyAction = "FikaHideoutCoopApplyUpgrade";
    public const string CompleteAction = "FikaHideoutCoopComplete";
    public const string RefundAction = "FikaHideoutCoopRefund";

    private const float AckTimeoutSeconds = 15f;
    private const float ApplyTimeoutSeconds = 25f;
    private const float CompleteTimeoutSeconds = 20f;

    private static readonly ManualLogSource _logger = Logger.CreateLogSource("Fika.HideoutCoopBuild");
    private static readonly ConcurrentDictionary<int, TaskCompletionSource<HideoutCoopBuildPacket>> _waits = new();
    private static readonly ConcurrentDictionary<int, PendingBuild> _pending = new();
    private static int _nextRequestId;

    public static void Reset()
    {
        foreach (var wait in _waits.Values)
        {
            wait.TrySetCanceled();
        }

        _waits.Clear();
        _pending.Clear();
    }

    public static void OnReceived(HideoutCoopBuildPacket packet, NetPeer peer = null)
    {
        if (packet == null)
        {
            return;
        }

        if (FikaHideoutCoop.IsHosting)
        {
            _ = HandleHost(packet, peer);
            return;
        }

        if (_waits.TryRemove(packet.RequestId, out var waiter))
        {
            waiter.TrySetResult(packet);
            return;
        }

        if (packet.Kind == EHideoutCoopBuildKind.Refund && packet.Items is { Length: > 0 })
        {
            _ = SendInventoryOperation(new CoopRefundPayload { Items = ToSpendItems(packet.Items) });
        }
    }

    public static async Task GuestUpgradeAction(AreaData area)
    {
        if (area?._actionGoingStatus == 1)
        {
            return;
        }

        area._actionGoingStatus = 1;
        try
        {
            var token = area._compositeDisposable.CancellationToken;
            switch (area.Status)
            {
                case EAreaStatus.ReadyToConstruct:
                case EAreaStatus.ReadyToUpgrade:
                    if (!await GuestStartUpgrade(area))
                    {
                        Notify("Host rejected hideout construction.");
                        return;
                    }

                    if (!token.IsCancellationRequested)
                    {
                        await area.WaitUpgradeTime(area.NextStage.ConstructionTime.Data, token);
                    }

                    break;
                case EAreaStatus.ReadyToInstallConstruct:
                case EAreaStatus.ReadyToInstallUpgrade:
                    if (!Singleton<HideoutRepresentation>.Instantiated)
                    {
                        return;
                    }

                    await Singleton<HideoutRepresentation>.Instance.CompleteUpgradeZone(area.Template.Type);
                    if (!token.IsCancellationRequested)
                    {
                        area.Upgrade();
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Guest hideout upgrade failed: {ex}");
            Notify("Hideout construction failed.");
        }
        finally
        {
            area._actionGoingStatus = 0;
        }
    }

    public static async Task GuestUpgradeZone(HideoutRepresentation representation, EAreaType areaType, RelatedRequirements requirements)
    {
        var area = FindArea(representation, areaType);
        if (area == null || !await GuestStartUpgrade(area, requirements))
        {
            throw new InvalidOperationException("Hideout coop upgrade was rejected.");
        }
    }

    public static async Task GuestCompleteZone(HideoutRepresentation representation, EAreaType areaType)
    {
        if (!await GuestComplete(representation, areaType))
        {
            throw new InvalidOperationException("Hideout coop complete was rejected.");
        }
    }

    private static async Task<bool> GuestStartUpgrade(AreaData area, RelatedRequirements requirements = null)
    {
        if (!Singleton<HideoutRepresentation>.Instantiated)
        {
            return false;
        }

        var representation = Singleton<HideoutRepresentation>.Instance;
        requirements ??= area.NextStage.Requirements;
        var itemRequirements = requirements.OfType<ItemRequirement>().ToArray();
        var references = representation.GetItemReferences(itemRequirements);
        if (!CoversRequirements(itemRequirements, references))
        {
            Notify("Not enough items in stash for this upgrade.");
            return false;
        }

        var items = references.Select(reference => new HideoutCoopBuildItem
        {
            Id = reference.Id ?? "",
            TemplateId = reference.TemplateId ?? "",
            Count = reference.Count,
            IsTool = reference.IsTool
        }).ToArray();

        var requestId = Interlocked.Increment(ref _nextRequestId);
        Send(new HideoutCoopBuildPacket
        {
            Kind = EHideoutCoopBuildKind.Request,
            RequestId = requestId,
            AreaType = (int)area.Template.Type,
            Items = items
        });

        var ack = await WaitFor(requestId, AckTimeoutSeconds);
        if (ack == null || ack.Kind != EHideoutCoopBuildKind.Ack || !ack.Ok)
        {
            _logger.LogWarning($"Hideout coop Ack failed: {ack?.Error}");
            return false;
        }

        var operations = representation.ResolveReferenceAction(references);
        if (!await SendInventoryOperation(new CoopSpendPayload { Items = ToSpendItems(items) }))
        {
            operations?.RollBack();
            representation.method_24();
            return false;
        }

        Send(new HideoutCoopBuildPacket
        {
            Kind = EHideoutCoopBuildKind.Spent,
            RequestId = requestId,
            AreaType = (int)area.Template.Type,
            Items = items
        });

        var applied = await WaitFor(requestId, ApplyTimeoutSeconds);
        if (applied == null || applied.Kind != EHideoutCoopBuildKind.ApplyOk || !applied.Ok)
        {
            await SendInventoryOperation(new CoopRefundPayload { Items = ToSpendItems(items) });
            operations?.RollBack();
            representation.method_24();
            return false;
        }

        BeginConstructionVisual(area);
        return true;
    }

    private static async Task<bool> GuestComplete(HideoutRepresentation representation, EAreaType areaType)
    {
        var requestId = Interlocked.Increment(ref _nextRequestId);
        Send(new HideoutCoopBuildPacket
        {
            Kind = EHideoutCoopBuildKind.Complete,
            RequestId = requestId,
            AreaType = (int)areaType
        });

        var ack = await WaitFor(requestId, CompleteTimeoutSeconds);
        if (ack == null || ack.Kind != EHideoutCoopBuildKind.CompleteAck || !ack.Ok)
        {
            _logger.LogWarning($"Hideout coop CompleteAck failed: {ack?.Error}");
            return false;
        }

        return true;
    }

    private static async Task HandleHost(HideoutCoopBuildPacket packet, NetPeer peer)
    {
        try
        {
            switch (packet.Kind)
            {
                case EHideoutCoopBuildKind.Request:
                    HandleHostRequest(packet, peer);
                    break;
                case EHideoutCoopBuildKind.Spent:
                    await HandleHostSpent(packet, peer);
                    break;
                case EHideoutCoopBuildKind.Complete:
                    await HandleHostComplete(packet, peer);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Host hideout coop packet {packet.Kind} failed: {ex}");
            if (peer != null)
            {
                Reply(peer, packet, packet.Kind == EHideoutCoopBuildKind.Complete
                    ? EHideoutCoopBuildKind.CompleteAck
                    : EHideoutCoopBuildKind.Ack, false, ex.Message);
            }
        }
    }

    private static void HandleHostRequest(HideoutCoopBuildPacket packet, NetPeer peer)
    {
        if (peer == null)
        {
            return;
        }

        if (!TryValidateUpgrade(packet, out var error))
        {
            Reply(peer, packet, EHideoutCoopBuildKind.Ack, false, error ?? "Invalid build request.");
            return;
        }

        _pending[packet.RequestId] = new PendingBuild
        {
            Peer = peer,
            AreaType = packet.AreaType,
            Items = packet.Items ?? []
        };
        Reply(peer, packet, EHideoutCoopBuildKind.Ack, true);
    }

    private static async Task HandleHostSpent(HideoutCoopBuildPacket packet, NetPeer peer)
    {
        if (!_pending.TryGetValue(packet.RequestId, out var pending) || pending.Peer?.Id != peer?.Id)
        {
            Reply(peer, packet, EHideoutCoopBuildKind.Refund, false, "No matching build request.", packet.Items);
            return;
        }

        if (!await SendInventoryOperation(new CoopApplyPayload { AreaType = packet.AreaType }))
        {
            _pending.TryRemove(packet.RequestId, out _);
            Reply(peer, packet, EHideoutCoopBuildKind.Refund, false, "Host hideout apply failed.", pending.Items);
            return;
        }

        pending.Applied = true;
        BeginHostConstruction((EAreaType)packet.AreaType);
        Reply(peer, packet, EHideoutCoopBuildKind.ApplyOk, true);
    }

    private static async Task HandleHostComplete(HideoutCoopBuildPacket packet, NetPeer peer)
    {
        if (!TryValidateComplete(packet, out var error) || !Singleton<HideoutRepresentation>.Instantiated)
        {
            Reply(peer, packet, EHideoutCoopBuildKind.CompleteAck, false, error ?? "Invalid complete.");
            return;
        }

        if (!await SendInventoryOperation(new CoopCompletePayload { AreaType = packet.AreaType }))
        {
            Reply(peer, packet, EHideoutCoopBuildKind.CompleteAck, false, "Host hideout complete failed.");
            return;
        }

        var area = FindArea(Singleton<HideoutRepresentation>.Instance, (EAreaType)packet.AreaType);
        if (area != null)
        {
            area.Upgrade();
        }

        _pending.TryRemove(packet.RequestId, out _);
        foreach (var key in _pending.Where(kv => kv.Value.AreaType == packet.AreaType).Select(kv => kv.Key).ToArray())
        {
            _pending.TryRemove(key, out _);
        }

        HideoutWorldSync.MarkDirty();
        Reply(peer, packet, EHideoutCoopBuildKind.CompleteAck, true);
    }

    private static bool TryValidateUpgrade(HideoutCoopBuildPacket packet, out string error)
    {
        error = null;
        if (!Singleton<HideoutRepresentation>.Instantiated)
        {
            error = "Hideout is not loaded.";
            return false;
        }

        var area = FindArea(Singleton<HideoutRepresentation>.Instance, (EAreaType)packet.AreaType);
        if (area == null)
        {
            error = "Area not found.";
            return false;
        }

        if (area.Status is not (EAreaStatus.ReadyToConstruct or EAreaStatus.ReadyToUpgrade))
        {
            error = $"Area status {area.Status} cannot start construction.";
            return false;
        }

        var requirements = area.NextStage?.Requirements?.OfType<ItemRequirement>().ToArray() ?? [];
        if (!CoversRequirements(requirements, packet.Items))
        {
            error = "Submitted items do not cover the recipe.";
            return false;
        }

        return true;
    }

    private static bool TryValidateComplete(HideoutCoopBuildPacket packet, out string error)
    {
        error = null;
        if (!Singleton<HideoutRepresentation>.Instantiated)
        {
            error = "Hideout is not loaded.";
            return false;
        }

        var area = FindArea(Singleton<HideoutRepresentation>.Instance, (EAreaType)packet.AreaType);
        if (area == null)
        {
            error = "Area not found.";
            return false;
        }

        if (area.Status is EAreaStatus.ReadyToInstallConstruct or EAreaStatus.ReadyToInstallUpgrade
            or EAreaStatus.Constructing or EAreaStatus.Upgrading or EAreaStatus.AutoUpgrading)
        {
            return true;
        }

        if (_pending.Values.Any(pending => pending.Applied && pending.AreaType == packet.AreaType))
        {
            return true;
        }

        error = $"Area status {area.Status} cannot complete construction.";
        return false;
    }

    private static void BeginHostConstruction(EAreaType areaType)
    {
        if (!Singleton<HideoutRepresentation>.Instantiated)
        {
            return;
        }

        var area = FindArea(Singleton<HideoutRepresentation>.Instance, areaType);
        if (area == null)
        {
            return;
        }

        BeginConstructionVisual(area);
        HideoutWorldSync.MarkDirty();
        var constructionTime = area.NextStage.ConstructionTime.Data;
        if (constructionTime >= float.Epsilon)
        {
            _ = area.WaitUpgradeTime(constructionTime, area._compositeDisposable.CancellationToken);
        }
    }

    private static void BeginConstructionVisual(AreaData area)
    {
        var current = area.CurrentStage;
        current.StartTime = DateTimeExtensions.UtcNow;
        current.ActionGoing = true;
        current.Waiting = true;
        area.Status = area.Status == EAreaStatus.ReadyToUpgrade || area.CurrentLevel > 0
            ? (area.NextStage.AutoUpgrade ? EAreaStatus.AutoUpgrading : EAreaStatus.Upgrading)
            : EAreaStatus.Constructing;
    }

    private static bool CoversRequirements(ItemRequirement[] requirements, IEnumerable<HideoutItemReference> references)
    {
        var items = references.Select(reference => new HideoutCoopBuildItem
        {
            TemplateId = reference.TemplateId ?? "",
            Count = reference.Count
        }).ToArray();
        return CoversRequirements(requirements, items);
    }

    private static bool CoversRequirements(ItemRequirement[] requirements, HideoutCoopBuildItem[] items)
    {
        var needed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var requirement in requirements ?? [])
        {
            if (string.IsNullOrEmpty(requirement.TemplateId))
            {
                continue;
            }

            needed.TryGetValue(requirement.TemplateId, out var current);
            needed[requirement.TemplateId] = current + requirement.IntCount;
        }

        var have = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items ?? [])
        {
            if (string.IsNullOrEmpty(item?.TemplateId))
            {
                continue;
            }

            have.TryGetValue(item.TemplateId, out var current);
            have[item.TemplateId] = current + item.Count;
        }

        foreach (var pair in needed)
        {
            have.TryGetValue(pair.Key, out var count);
            if (count < pair.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static AreaData FindArea(HideoutRepresentation representation, EAreaType areaType)
    {
        return representation?.AreaDatas?.FirstOrDefault(area => area?.Template != null && area.Template.Type == areaType);
    }

    private static void Send(HideoutCoopBuildPacket packet)
    {
        if (Singleton<FikaClient>.Instantiated)
        {
            Singleton<FikaClient>.Instance.SendData(ref packet, DeliveryMethod.ReliableOrdered, true);
            return;
        }

        if (Singleton<FikaServer>.Instantiated)
        {
            Singleton<FikaServer>.Instance.SendData(ref packet, DeliveryMethod.ReliableOrdered);
        }
    }

    private static void Reply(NetPeer peer, HideoutCoopBuildPacket source, EHideoutCoopBuildKind kind, bool ok,
        string error = "", HideoutCoopBuildItem[] items = null)
    {
        if (peer == null || !Singleton<FikaServer>.Instantiated)
        {
            return;
        }

        var packet = new HideoutCoopBuildPacket
        {
            Kind = kind,
            RequestId = source.RequestId,
            AreaType = source.AreaType,
            Ok = ok,
            Error = error ?? "",
            Items = items ?? []
        };
        Singleton<FikaServer>.Instance.SendDataToPeer(ref packet, DeliveryMethod.ReliableOrdered, peer);
    }

    private static async Task<HideoutCoopBuildPacket> WaitFor(int requestId, float seconds)
    {
        var waiter = new TaskCompletionSource<HideoutCoopBuildPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waits[requestId] = waiter;
        var completed = await Task.WhenAny(waiter.Task, Task.Delay(TimeSpan.FromSeconds(seconds)));
        _waits.TryRemove(requestId, out _);
        if (completed != waiter.Task)
        {
            return null;
        }

        return waiter.Task.Result;
    }

    private static Task<bool> SendInventoryOperation(object payload)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Singleton<ClientApplication<IEftSession>>.Instantiated)
        {
            tcs.TrySetResult(false);
            return tcs.Task;
        }

        try
        {
            Singleton<ClientApplication<IEftSession>>.Instance
                .GetClientBackEndSession()
                .SendOperationRightNow(payload, result => tcs.TrySetResult(result != null && !result.Failed));
        }
        catch (Exception ex)
        {
            _logger.LogError($"Hideout coop inventory operation failed: {ex}");
            tcs.TrySetResult(false);
        }

        return tcs.Task;
    }

    private static CoopSpendItem[] ToSpendItems(HideoutCoopBuildItem[] items)
    {
        return (items ?? []).Select(item => new CoopSpendItem
        {
            Id = item.Id,
            Template = item.TemplateId,
            Count = item.Count
        }).ToArray();
    }

    private static void Notify(string message)
    {
        try
        {
            NotificationManager.DisplayMessageNotification(message, iconType: ENotificationIconType.Alert);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Hideout coop notify failed: {ex.Message}");
        }
    }

    private sealed class PendingBuild
    {
        public NetPeer Peer;
        public int AreaType;
        public HideoutCoopBuildItem[] Items = [];
        public bool Applied;
    }

    private sealed class CoopSpendPayload : CommandWithOwner
    {
        [JsonProperty("Action")]
        public string Action = SpendAction;

        [JsonProperty("items")]
        public CoopSpendItem[] Items;
    }

    private sealed class CoopApplyPayload : CommandWithOwner
    {
        [JsonProperty("Action")]
        public string Action = ApplyAction;

        [JsonProperty("areaType")]
        public int AreaType;
    }

    private sealed class CoopCompletePayload : CommandWithOwner
    {
        [JsonProperty("Action")]
        public string Action = CompleteAction;

        [JsonProperty("areaType")]
        public int AreaType;
    }

    private sealed class CoopRefundPayload : CommandWithOwner
    {
        [JsonProperty("Action")]
        public string Action = RefundAction;

        [JsonProperty("items")]
        public CoopSpendItem[] Items;
    }

    private sealed class CoopSpendItem
    {
        [JsonProperty("id")]
        public string Id;

        [JsonProperty("_tpl")]
        public string Template;

        [JsonProperty("count")]
        public int Count;
    }
}
