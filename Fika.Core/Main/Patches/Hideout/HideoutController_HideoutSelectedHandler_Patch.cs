using System.Reflection;
using EFT;
using Fika.Core.Main.Components;
using SPT.Reflection.Patching;

namespace Fika.Core.Main.Patches.Hideout;

/// <summary>
/// 在 method_0 把 IsInHideout 置位、玩家还没 SetGuest 之前就锁死客人身份。
/// </summary>
public class HideoutController_HideoutSelectedHandler_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(TarkovApplication.HideoutController).GetMethod(
            nameof(TarkovApplication.HideoutController.HideoutSelectedHandler));
    }

    [PatchPrefix]
    public static void Prefix(HideoutData hideoutData)
    {
        FikaHideoutCoop.OnHideoutSelected(hideoutData);
    }
}
