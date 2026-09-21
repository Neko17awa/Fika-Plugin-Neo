using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using Comfort.Common;
using Diz.Jobs;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using Fika.Core.Main.BotClasses;
using Fika.Core.Main.ClientClasses;
using Fika.Core.Main.HostClasses;
using Fika.Core.Main.ObservedClasses;
using Fika.Core.Main.Players;
using Fika.Core.Main.Utils;
using Fika.Core.Networking;
using Fika.Core.Networking.Packets.Generic;
using Fika.Core.Networking.Packets.Generic.SubPackets;
using Fika.Core.Networking.Packets.Hideout;
using Fika.Core.Networking.Packets.Player.Common;

namespace Fika.Core.Main.Components;

/// <summary>
/// 藏身处手持/背包/地面物品同步。不替换 HideoutPlayer，走独立包。
/// </summary>
public static class HideoutItemSync
{
    public static bool IsApplying { get; private set; }

    private static readonly ManualLogSource _logger = Logger.CreateLogSource("Fika.HideoutItem");
    private static readonly Dictionary<int, HideoutItemPacket> _pending = [];

    public static void Reset()
    {
        IsApplying = false;
        _pending.Clear();
    }

    public static void Tick()
    {
        if (_pending.Count == 0)
        {
            return;
        }

        List<int> ready = null;
        foreach (var pair in _pending)
        {
            if (TryGetObserved(pair.Key, out _))
            {
                ready ??= [];
                ready.Add(pair.Key);
            }
        }

        if (ready == null)
        {
            return;
        }

        for (var i = 0; i < ready.Count; i++)
        {
            var netId = ready[i];
            if (_pending.TryGetValue(netId, out var packet))
            {
                _pending.Remove(netId);
                Apply(packet);
            }
        }
    }

    public static void SendToPeer(NetPeer peer)
    {
        if (peer == null || !Singleton<FikaServer>.Instantiated)
        {
            return;
        }

        var hands = CaptureHands();
        if (hands != null)
        {
            Singleton<FikaServer>.Instance.SendDataToPeer(ref hands, DeliveryMethod.ReliableOrdered, peer);
        }

        SendWorldLootToPeer(peer);
    }

    public static void SendCurrentState()
    {
        var hands = CaptureHands();
        if (hands != null)
        {
            Send(hands);
        }
    }

    public static void SendHands(Player player, Item item)
    {
        if (!CanSend(player))
        {
            return;
        }

        var packet = new HideoutItemPacket
        {
            Action = EHideoutItemAction.Hands,
            NetId = LocalNetId(),
            ProceedType = ProceedTypeFromItem(item),
            Item = item
        };
        Send(packet);
    }

    public static void SendEmptyHands(Player player)
    {
        if (!CanSend(player))
        {
            return;
        }

        Send(new HideoutItemPacket
        {
            Action = EHideoutItemAction.Hands,
            NetId = LocalNetId(),
            ProceedType = EProceedType.EmptyHands
        });
    }

    public static void SendUnequipHands(Player player, bool fastDrop)
    {
        if (!CanSend(player))
        {
            return;
        }

        Send(new HideoutItemPacket
        {
            Action = EHideoutItemAction.UnequipHands,
            NetId = LocalNetId(),
            FastDrop = fastDrop
        });
    }

    public static void SendDroppedLoot(LootItem loot, bool placed)
    {
        if (!FikaHideoutCoop.IsActive || IsApplying || loot?.Item == null)
        {
            return;
        }

        var owner = loot.LastOwner as Player;
        if (owner != null && !owner.IsYourPlayer)
        {
            return;
        }

        var transform = loot.transform;
        Send(new HideoutItemPacket
        {
            Action = placed ? EHideoutItemAction.Place : EHideoutItemAction.Drop,
            NetId = LocalNetId(),
            Item = loot.Item,
            LootId = loot.ItemId ?? loot.Item.Id,
            Position = transform.position,
            Rotation = transform.rotation,
            Velocity = loot.RigidBody != null ? loot.RigidBody.velocity : Vector3.zero,
            AngularVelocity = loot.RigidBody != null ? loot.RigidBody.angularVelocity : Vector3.zero
        });
    }

    public static void SendPickup(string lootId)
    {
        if (!FikaHideoutCoop.IsActive || IsApplying || string.IsNullOrEmpty(lootId))
        {
            return;
        }

        Send(new HideoutItemPacket
        {
            Action = EHideoutItemAction.Pickup,
            NetId = LocalNetId(),
            LootId = lootId
        });
    }

    public static void SendInventory(ItemController controller, EFT.InventoryLogic.Operations.AbstractOperation operation)
    {
        if (!FikaHideoutCoop.IsActive || IsApplying || controller == null || operation == null)
        {
            return;
        }

        if (controller is ObservedInventoryController or HostInventoryController
            or ClientInventoryController or BotInventoryController)
        {
            return;
        }

        if (controller is Player.PlayerInventoryController playerInv
            && playerInv.Player != null && !playerInv.Player.IsYourPlayer)
        {
            return;
        }

        if (operation is SinglePlayerSearchContentOperation or SetDialogProgressOperation)
        {
            return;
        }

        var netId = LocalNetId();
        if (netId <= 0 || !Singleton<IFikaNetworkManager>.Instantiated)
        {
            return;
        }

        try
        {
            var manager = Singleton<IFikaNetworkManager>.Instance;
            manager.SendGenericPacket(EGenericSubPacketType.InventoryOperation,
                InventoryPacket.FromValue(netId, operation), Singleton<FikaServer>.Instantiated);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Hideout inventory send failed: {ex.Message}");
        }
    }

    public static void Apply(HideoutItemPacket packet)
    {
        if (packet == null || !FikaHideoutCoop.IsActive)
        {
            return;
        }

        if (packet.NetId == LocalNetId())
        {
            return;
        }

        switch (packet.Action)
        {
            case EHideoutItemAction.Hands:
            case EHideoutItemAction.UnequipHands:
                ApplyHandsOrQueue(packet);
                break;
            case EHideoutItemAction.Drop:
            case EHideoutItemAction.Place:
                StaticManager.BeginCoroutine(ApplyWorldItemRoutine(packet));
                break;
            case EHideoutItemAction.Pickup:
                ApplyPickup(packet);
                break;
        }
    }

    private static void ApplyHandsOrQueue(HideoutItemPacket packet)
    {
        if (!TryGetObserved(packet.NetId, out var observed))
        {
            _pending[packet.NetId] = packet.Clone();
            return;
        }

        try
        {
            IsApplying = true;
            if (packet.Action == EHideoutItemAction.UnequipHands)
            {
                observed.HandleDropPacket(packet.FastDrop);
                return;
            }

            var item = ResolveItem(observed, packet.Item);
            if (packet.ProceedType is not EProceedType.EmptyHands && item == null)
            {
                _logger.LogWarning($"Hideout hands missing item netId={packet.NetId} type={packet.ProceedType}");
                observed.HandleHideoutHands(EProceedType.EmptyHands, null);
                return;
            }

            if (item != null)
            {
                StaticManager.BeginCoroutine(LoadThen(item, () =>
                {
                    if (TryGetObserved(packet.NetId, out var player))
                    {
                        var applying = IsApplying;
                        IsApplying = true;
                        try
                        {
                            player.HandleHideoutHands(packet.ProceedType, ResolveItem(player, item));
                        }
                        finally
                        {
                            IsApplying = applying;
                        }
                    }
                }));
                return;
            }

            observed.HandleHideoutHands(packet.ProceedType, null);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Apply hideout hands failed: {ex.Message}");
        }
        finally
        {
            IsApplying = false;
        }
    }

    private static IEnumerator ApplyWorldItemRoutine(HideoutItemPacket packet)
    {
        if (packet.Item == null || !Singleton<GameWorld>.Instantiated)
        {
            yield break;
        }

        if (FindLoot(packet.LootId, packet.Item.Id) != null)
        {
            yield break;
        }

        yield return LoadThen(packet.Item, () =>
        {
            if (!Singleton<GameWorld>.Instantiated)
            {
                return;
            }

            if (FindLoot(packet.LootId, packet.Item.Id) != null)
            {
                return;
            }

            TryGetObserved(packet.NetId, out var observed);
            IPlayer thrower = observed != null
                ? observed
                : Singleton<GameWorld>.Instance.MainPlayer;
            if (thrower == null)
            {
                return;
            }

            IsApplying = true;
            try
            {
                if (packet.Action == EHideoutItemAction.Place)
                {
                    Singleton<GameWorld>.Instance.SetupItem(packet.Item, thrower, packet.Position, packet.Rotation);
                }
                else
                {
                    Singleton<GameWorld>.Instance.ThrowItem(packet.Item, thrower, packet.Position, packet.Rotation,
                        packet.Velocity, packet.AngularVelocity, syncable: false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Apply hideout world item failed: {ex.Message}");
            }
            finally
            {
                IsApplying = false;
            }
        });
    }

    private static void ApplyPickup(HideoutItemPacket packet)
    {
        if (!Singleton<GameWorld>.Instantiated)
        {
            return;
        }

        IsApplying = true;
        try
        {
            Singleton<GameWorld>.Instance.DestroyLoot(packet.LootId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Apply hideout pickup failed: {ex.Message}");
        }
        finally
        {
            IsApplying = false;
        }
    }

    private static HideoutItemPacket CaptureHands()
    {
        var player = LocalPlayer();
        var netId = LocalNetId();
        if (player == null || netId <= 0)
        {
            return null;
        }

        var item = player.HandsController != null ? player.HandsController.Item : null;
        if (item == null)
        {
            return new HideoutItemPacket
            {
                Action = EHideoutItemAction.Hands,
                NetId = netId,
                ProceedType = EProceedType.EmptyHands
            };
        }

        return new HideoutItemPacket
        {
            Action = EHideoutItemAction.Hands,
            NetId = netId,
            ProceedType = ProceedTypeFromItem(item),
            Item = item
        };
    }

    private static void SendWorldLootToPeer(NetPeer peer)
    {
        if (!Singleton<GameWorld>.Instantiated)
        {
            return;
        }

        var world = Singleton<GameWorld>.Instance;
        for (var i = 0; i < world.LootList.Count; i++)
        {
            if (world.LootList[i] is not LootItem loot || loot.Item == null)
            {
                continue;
            }

            if (loot.LastOwner == null && loot.RigidBody == null)
            {
                continue;
            }

            var placed = loot.RigidBody == null || loot.RigidBody.isKinematic;
            var packet = new HideoutItemPacket
            {
                Action = placed ? EHideoutItemAction.Place : EHideoutItemAction.Drop,
                NetId = LocalNetId(),
                Item = loot.Item,
                LootId = loot.ItemId ?? loot.Item.Id,
                Position = loot.transform.position,
                Rotation = loot.transform.rotation,
                Velocity = loot.RigidBody != null ? loot.RigidBody.velocity : Vector3.zero,
                AngularVelocity = loot.RigidBody != null ? loot.RigidBody.angularVelocity : Vector3.zero
            };
            Singleton<FikaServer>.Instance.SendDataToPeer(ref packet, DeliveryMethod.ReliableOrdered, peer);
        }
    }

    private static void Send(HideoutItemPacket packet)
    {
        if (packet == null || !Singleton<IFikaNetworkManager>.Instantiated)
        {
            return;
        }

        Singleton<IFikaNetworkManager>.Instance.SendData(ref packet, DeliveryMethod.ReliableOrdered, true);
    }

    private static bool CanSend(Player player)
    {
        return FikaHideoutCoop.IsActive && !IsApplying && player != null && player.IsYourPlayer && LocalNetId() > 0;
    }

    private static int LocalNetId()
    {
        return Singleton<IFikaNetworkManager>.Instantiated ? Singleton<IFikaNetworkManager>.Instance.NetId : 0;
    }

    private static Player LocalPlayer()
    {
        if (!Singleton<GameWorld>.Instantiated)
        {
            return null;
        }

        var world = Singleton<GameWorld>.Instance;
        return world is HideoutGameWorld ? world.MainPlayer : null;
    }

    private static bool TryGetObserved(int netId, out ObservedPlayer observed)
    {
        observed = null;
        if (!Singleton<IFikaNetworkManager>.Instantiated)
        {
            return false;
        }

        var players = Singleton<IFikaNetworkManager>.Instance.CoopHandler?.Players;
        if (players != null && players.TryGetValue(netId, out var player) && player is ObservedPlayer obs)
        {
            observed = obs;
            return true;
        }

        return false;
    }

    private static Item ResolveItem(ObservedPlayer observed, Item item)
    {
        if (item == null)
        {
            return null;
        }

        var existing = observed.FindItemById(item.Id, false, false);
        return existing.Succeeded ? existing.Value : item;
    }

    private static LootItem FindLoot(string lootId, string itemId)
    {
        if (!Singleton<GameWorld>.Instantiated)
        {
            return null;
        }

        var world = Singleton<GameWorld>.Instance;
        for (var i = 0; i < world.LootList.Count; i++)
        {
            if (world.LootList[i] is not LootItem loot)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(lootId) && loot.ItemId == lootId)
            {
                return loot;
            }

            if (!string.IsNullOrEmpty(itemId) && loot.Item != null && loot.Item.Id == itemId)
            {
                return loot;
            }
        }

        return null;
    }

    private static IEnumerator LoadThen(Item item, Action next)
    {
        if (item != null && Singleton<ObjectsFactory>.Instantiated)
        {
            List<ResourceKey> collection = [];
            foreach (var subItem in item.GetAllItems())
            {
                if (subItem?.Template?.AllResources != null)
                {
                    collection.AddRange(subItem.Template.AllResources);
                }
            }

            if (collection.Count > 0)
            {
                var loadTask = Singleton<ObjectsFactory>.Instance.LoadBundlesAndCreatePools(
                    ObjectsFactory.PoolsCategory.Raid, ObjectsFactory.AssemblyType.Online,
                    [.. collection], JobYieldPriority.Immediate, null, default);
                var wait = new WaitForEndOfFrame();
                while (!loadTask.IsCompleted)
                {
                    yield return wait;
                }
            }
        }

        next();
    }

    public static EProceedType ProceedTypeFromItem(Item item)
    {
        if (item == null)
        {
            return EProceedType.EmptyHands;
        }

        if (item is Weapon weapon)
        {
            return weapon.IsStationaryWeapon ? EProceedType.Stationary : EProceedType.Weapon;
        }

        if (item is ThrowWeap)
        {
            return EProceedType.GrenadeClass;
        }

        if (item is Meds)
        {
            return EProceedType.MedsClass;
        }

        if (item is FoodDrink)
        {
            return EProceedType.FoodClass;
        }

        if (item.GetItemComponent<KnifeComponent>() != null)
        {
            return EProceedType.Knife;
        }

        if (item is PortableRangeFinder or RadioTransmitter)
        {
            return EProceedType.UsableItem;
        }

        if (item.UsePrefab != null)
        {
            return EProceedType.QuickUse;
        }

        return EProceedType.Weapon;
    }
}
