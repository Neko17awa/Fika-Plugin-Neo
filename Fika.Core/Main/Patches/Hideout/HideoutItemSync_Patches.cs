using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using Fika.Core.Main.Components;
using SPT.Reflection.Patching;

namespace Fika.Core.Main.Patches.Hideout;

public class Player_SetItemInHands_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(Player).GetMethod(nameof(Player.SetItemInHands));
    }

    [PatchPostfix]
    public static void Postfix(Player __instance, Item item)
    {
        HideoutItemSync.SendHands(__instance, item);
    }
}

public class Player_SetEmptyHands_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(Player).GetMethod(nameof(Player.SetEmptyHands));
    }

    [PatchPostfix]
    public static void Postfix(Player __instance)
    {
        HideoutItemSync.SendEmptyHands(__instance);
    }
}

public class Player_DropCurrentController_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(Player).GetMethod(nameof(Player.DropCurrentController));
    }

    [PatchPostfix]
    public static void Postfix(Player __instance, bool fastDrop)
    {
        HideoutItemSync.SendUnequipHands(__instance, fastDrop);
    }
}

public class ItemController_Execute_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(ItemController).GetMethod(nameof(ItemController.Execute),
            [typeof(EFT.InventoryLogic.Operations.AbstractOperation), typeof(Callback)]);
    }

    [PatchPostfix]
    public static void Postfix(ItemController __instance, EFT.InventoryLogic.Operations.AbstractOperation operation)
    {
        HideoutItemSync.SendInventory(__instance, operation);
    }
}

public class GameWorld_ThrowItem_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(GameWorld).GetMethod(nameof(GameWorld.ThrowItem),
        [
            typeof(Item), typeof(IPlayer), typeof(Vector3), typeof(Quaternion),
            typeof(Vector3), typeof(Vector3), typeof(bool), typeof(bool), typeof(float)
        ]);
    }

    [PatchPostfix]
    public static void Postfix(LootItem __result)
    {
        HideoutItemSync.SendDroppedLoot(__result, placed: false);
    }
}

public class GameWorld_SetupItem_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(GameWorld).GetMethod(nameof(GameWorld.SetupItem));
    }

    [PatchPostfix]
    public static void Postfix(LootItem __result)
    {
        HideoutItemSync.SendDroppedLoot(__result, placed: true);
    }
}

public class LootItem_RemoveLootItem_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(LootItem).GetMethod(nameof(LootItem.RemoveLootItem));
    }

    [PatchPostfix]
    public static void Postfix(LootItem __instance, RemoveItemEventArgs args)
    {
        if (args == null || args.Status != CommandStatus.Succeed)
        {
            return;
        }

        HideoutItemSync.SendPickup(__instance.ItemId);
    }
}

public class HideoutPlayer_SetPatrol_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(HideoutPlayer).GetMethod(nameof(HideoutPlayer.SetPatrol));
    }

    [PatchPostfix]
    public static void Postfix(HideoutPlayer __instance)
    {
        HideoutItemSync.OnPatrolChanged(__instance);
    }
}
