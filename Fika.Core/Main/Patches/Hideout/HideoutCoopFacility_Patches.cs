using System.Linq;
using System.Reflection;
using EFT;
using EFT.Hideout;
using EFT.UI;
using Fika.Core.Main.Components;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Fika.Core.Main.Patches.Hideout;

/// <summary>
/// Fika 参观时恢复区域图标和镜头选择；仓库 / 转移 / 发电机 / 装饰仍关闭。
/// </summary>
public class HideoutScreenOverlay_Show_CoopFacility_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(HideoutScreenOverlay).GetMethod(nameof(HideoutScreenOverlay.Show));
    }

    [PatchPostfix]
    public static void Postfix(HideoutScreenOverlay __instance, AreaData[] areaDatas)
    {
        if (!FikaHideoutCoop.IsVisitGuest)
        {
            return;
        }

        Traverse.Create(__instance).Field("_canSelectArea").SetValue(true);
        if (__instance._areasPanel != null)
        {
            __instance._areasPanel.Show(areaDatas, arg =>
            {
                _ = __instance.AreaSelected(arg.Data, wait: true);
            });
        }

        if (__instance._nightVisionButton != null && !__instance._nightVisionButton.isActiveAndEnabled)
        {
            __instance.EnableNightVisionButton();
        }
    }
}

/// <summary>
/// 原版参观把世界图标全部藏掉，Fika 客人需要能点到设施。
/// </summary>
public class HideoutScreenRear_UpdateAreaIconsVisibility_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(HideoutScreenRear).GetMethod(nameof(HideoutScreenRear.UpdateAreaIconsVisibility));
    }

    [PatchPostfix]
    public static void Postfix(HideoutScreenRear __instance)
    {
        if (!FikaHideoutCoop.IsVisitGuest)
        {
            return;
        }

        var traverse = Traverse.Create(__instance);
        var dataSelected = traverse.Field("_dataSelected").GetValue<bool>();
        var firstPersonIcons = traverse.Field("_firstPersonIconsVisibility").GetValue<bool>();
        var visible = !dataSelected && (firstPersonIcons || !__instance.FirstPerson);
        __instance.SetAreaIconsVisible(visible);
    }
}

/// <summary>
/// 放开「切到该设施」镜头；继续禁止开仓库和区域物品转移。
/// </summary>
public class InteractionContextHelper_GetAvailableHideoutActions_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(InteractionContextHelper).GetMethod(
            nameof(InteractionContextHelper.GetAvailableActions),
            [typeof(HideoutPlayerOwner), typeof(HideoutArea)]);
    }

    [PatchPostfix]
    public static void Postfix(HideoutPlayerOwner owner, HideoutArea hideoutArea, ref AvailableInteractionState __result)
    {
        if (__result?.Actions == null || hideoutArea?.Data == null || !FikaHideoutCoop.IsVisitGuest)
        {
            return;
        }

        if (owner.InShootingRange)
        {
            return;
        }

        if (__result.Actions.Any(action => action?.Name != null && action.Name.StartsWith("Switch to")))
        {
            return;
        }

        var areaName = hideoutArea.Data.Template.Type.ToString().Localized();
        __result.Actions.Add(new InteractionAction
        {
            Name = string.Format("Switch to ({0})".Localized(), areaName),
            Action = () => owner.SelectArea(hideoutArea)
        });
    }
}

/// <summary>
/// 访客用自己的技能/仓库重算会把 host 同步来的 Status 盖掉；施工中的 ActionGoing 仍走原逻辑以便完工。
/// </summary>
public class AreaData_DecideStatus_VisitGuest_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(AreaData).GetMethod(nameof(AreaData.DecideStatus));
    }

    [PatchPrefix]
    public static bool Prefix(AreaData __instance)
    {
        if (!FikaHideoutCoop.IsVisitGuest)
        {
            return true;
        }

        return __instance.CurrentStage != null && __instance.CurrentStage.ActionGoing;
    }
}

/// <summary>
/// 全图 DecideStatus 会按访客档案重算，参观时跳过。
/// </summary>
public class HideoutRepresentation_method_37_VisitGuest_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(HideoutRepresentation).GetMethod("method_37", BindingFlags.Instance | BindingFlags.Public);
    }

    [PatchPrefix]
    public static bool Prefix()
    {
        return !FikaHideoutCoop.IsVisitGuest;
    }
}
