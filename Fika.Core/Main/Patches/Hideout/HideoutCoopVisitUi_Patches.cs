using System.Reflection;
using System.Threading.Tasks;
using EFT;
using EFT.Hideout;
using EFT.UI;
using Fika.Core.Main.Components;
using SPT.Reflection.Patching;

namespace Fika.Core.Main.Patches.Hideout;

/// <summary>
/// 参观 HUD 标题改成「某某的藏身处」。
/// </summary>
public class HideoutScreenRear_Show_VisitOwner_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(HideoutScreenRear).GetMethod(nameof(HideoutScreenRear.Show));
    }

    [PatchPostfix]
    public static void Postfix(HideoutScreenRear __instance, Task __result)
    {
        if (!FikaHideoutCoop.IsVisitGuest)
        {
            return;
        }

        _ = ApplyWhenReady(__instance, __result);
    }

    private static async Task ApplyWhenReady(HideoutScreenRear rear, Task show)
    {
        if (show != null)
        {
            await show;
        }

        if (rear == null || rear._hideoutText == null || !FikaHideoutCoop.IsVisitGuest)
        {
            return;
        }

        rear._hideoutText.text = HideoutCoopVisitUi.Title();
        rear._hideoutText.color = HideoutCoopVisitUi.TitleColor;
    }
}

/// <summary>
/// 设施面板建造/升级/安装按钮带主人昵称，并提示材料从访客仓库扣。
/// </summary>
public class AreaScreenSubstrate_method_5_VisitOwner_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(AreaScreenSubstrate).GetMethod("method_5", BindingFlags.Instance | BindingFlags.Public);
    }

    [PatchPostfix]
    public static void Postfix(AreaScreenSubstrate __instance)
    {
        if (!FikaHideoutCoop.IsVisitGuest || __instance._actionButton == null)
        {
            return;
        }

        var key = __instance._actionButton.HeaderText;
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        var tooltip = HideoutCoopVisitUi.Tooltip();
        __instance._actionButton.SetRawText(HideoutCoopVisitUi.Action(key.Localized()), 20);
        __instance._actionButton.SetTooltips(tooltip, tooltip);
    }
}

/// <summary>
/// 配方需求标题带主人昵称。
/// </summary>
public class RequirementsPanel_ShowContents_VisitOwner_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RequirementsPanel).GetMethod(nameof(RequirementsPanel.ShowContents));
    }

    [PatchPostfix]
    public static void Postfix(RequirementsPanel __instance, Task __result)
    {
        if (!FikaHideoutCoop.IsVisitGuest)
        {
            return;
        }

        _ = ApplyWhenReady(__instance, __result);
    }

    private static async Task ApplyWhenReady(RequirementsPanel panel, Task show)
    {
        if (show != null)
        {
            await show;
        }

        if (panel == null || panel._requirementsLabel == null || !FikaHideoutCoop.IsVisitGuest)
        {
            return;
        }

        panel._requirementsLabel.text = HideoutCoopVisitUi.Requirements(panel._requirementsLabel.text);
        panel._requirementsLabel.color = HideoutCoopVisitUi.TitleColor;
    }
}

/// <summary>
/// 设施详情卡名称前加上主人，世界图标不改以免刷屏。
/// </summary>
public class AreaPanel_SetInfo_VisitOwner_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(AreaPanel).GetMethod(nameof(AreaPanel.SetInfo));
    }

    [PatchPostfix]
    public static void Postfix(AreaPanel __instance)
    {
        if (!FikaHideoutCoop.IsVisitGuest || __instance is AreaWorldPanel || __instance.AreaName == null || __instance.Data?.Template == null)
        {
            return;
        }

        __instance.AreaName.text = HideoutCoopVisitUi.AreaName(__instance.Data.Template.Name);
        __instance.AreaName.color = HideoutCoopVisitUi.TitleColor;
    }
}
