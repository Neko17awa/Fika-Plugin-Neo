using System.Reflection;
using EFT;
using EFT.Hideout;
using Fika.Core.Main.Components;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Fika.Core.Main.Patches.Hideout;

/// <summary>
/// 主人改灯光后广播；客人改灯光不写本地 prefs，视觉只跟主人包。
/// </summary>
public class HideoutController_SetLightLevel_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(HideoutController).GetMethod(nameof(HideoutController.SetLightLevel));
    }

    [PatchPrefix]
    public static bool Prefix()
    {
        return !HideoutWorldSync.IsApplying && !FikaHideoutCoop.IsVisitGuest;
    }

    [PatchPostfix]
    public static void Postfix()
    {
        HideoutWorldSync.MarkDirty();
    }
}

/// <summary>
/// 主人开关发电机后广播；参观中客人的开关不改本地供电。
/// </summary>
public class EnergyController_SetSwitchedStatus_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(EnergyController).GetMethod(nameof(EnergyController.SetSwitchedStatus));
    }

    [PatchPrefix]
    public static bool Prefix()
    {
        if (HideoutWorldSync.IsApplying)
        {
            return true;
        }

        return !FikaHideoutCoop.IsVisitGuest;
    }

    [PatchPostfix]
    public static void Postfix()
    {
        HideoutWorldSync.MarkDirty();
    }
}

public class AreaData_SetCurrentLevel_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(AreaData).GetMethod(nameof(AreaData.SetCurrentLevel), [typeof(int), typeof(bool)]);
    }

    [PatchPostfix]
    public static void Postfix()
    {
        HideoutWorldSync.MarkDirty();
    }
}

public class AreaData_SetCurrentLevelDumb_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(AreaData).GetMethod(nameof(AreaData.SetCurrentLevelDumb), [typeof(int), typeof(bool)]);
    }

    [PatchPostfix]
    public static void Postfix()
    {
        HideoutWorldSync.MarkDirty();
    }
}

public class HideoutCustomizationController_InstallCustomization_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(HideoutCustomizationController).GetMethod(
            nameof(HideoutCustomizationController.InstallCustomization),
            [typeof(MongoID), typeof(EHideoutCustomizationType)]);
    }

    [PatchPostfix]
    public static void Postfix()
    {
        HideoutWorldSync.MarkDirty();
    }
}

/// <summary>
/// 客人点选装饰会走 Offer 并写自己的档案；参观时禁止这条路径。
/// </summary>
public class HideoutCustomizationController_InstallCustomizationOffer_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(HideoutCustomizationController).GetMethod(
            nameof(HideoutCustomizationController.InstallCustomization),
            [typeof(HideoutCustomizationOffer)]);
    }

    [PatchPrefix]
    public static bool Prefix()
    {
        return !FikaHideoutCoop.IsVisitGuest;
    }
}

/// <summary>
/// 参观端 HideoutCameraController / AmbianceController 可能尚未挂上，原方法会空引用。
/// </summary>
public class HideoutController_UpdateCameraFlashlight_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(HideoutController).GetMethod(nameof(HideoutController.UpdateCameraFlashlight));
    }

    [PatchPrefix]
    public static bool Prefix(HideoutController __instance)
    {
        var traverse = Traverse.Create(__instance);
        var camera = traverse.Field("_hideoutCameraController").GetValue<HideoutCameraController>();
        var ambiance = traverse.Field("_ambianceController").GetValue<AmbianceController>();
        return camera != null && ambiance != null;
    }
}

public class HideoutController_EnergySupplyChanged_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(HideoutController).GetMethod(nameof(HideoutController.EnergySupplyChanged));
    }

    [PatchPrefix]
    public static bool Prefix(HideoutController __instance)
    {
        return Traverse.Create(__instance).Field("_ambianceController").GetValue<AmbianceController>() != null;
    }
}
