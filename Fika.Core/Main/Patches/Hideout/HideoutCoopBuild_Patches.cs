using System.Reflection;
using System.Threading.Tasks;
using EFT;
using EFT.Hideout;
using Fika.Core.Main.Components;
using SPT.Reflection.Patching;

namespace Fika.Core.Main.Patches.Hideout;

/// <summary>
/// 访客建造走 Fika 两阶段协议，不把 HTTP HideoutUpgrade 打到自己的 SPT。
/// </summary>
public class AreaData_UpgradeAction_CoopBuild_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(AreaData).GetMethod(nameof(AreaData.UpgradeAction));
    }

    [PatchPrefix]
    public static bool Prefix(AreaData __instance, ref Task __result)
    {
        if (!FikaHideoutCoop.IsVisitGuest)
        {
            return true;
        }

        __result = HideoutCoopBuild.GuestUpgradeAction(__instance);
        return false;
    }
}

public class HideoutRepresentation_UpgradeZone_CoopBuild_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(HideoutRepresentation).GetMethod(nameof(HideoutRepresentation.UpgradeZone));
    }

    [PatchPrefix]
    public static bool Prefix(HideoutRepresentation __instance, EAreaType areaType, RelatedRequirements requirements, ref Task __result)
    {
        if (!FikaHideoutCoop.IsVisitGuest)
        {
            return true;
        }

        __result = HideoutCoopBuild.GuestUpgradeZone(__instance, areaType, requirements);
        return false;
    }
}

public class HideoutRepresentation_CompleteUpgradeZone_CoopBuild_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(HideoutRepresentation).GetMethod(nameof(HideoutRepresentation.CompleteUpgradeZone));
    }

    [PatchPrefix]
    public static bool Prefix(HideoutRepresentation __instance, EAreaType areaType, ref Task __result)
    {
        if (!FikaHideoutCoop.IsVisitGuest)
        {
            return true;
        }

        __result = HideoutCoopBuild.GuestCompleteZone(__instance, areaType);
        return false;
    }
}
