using System.Reflection;
using EFT;
using Fika.Core.Main.Components;
using SPT.Reflection.Patching;

namespace Fika.Core.Main.Patches.Hideout;

public class HideoutController_UnloadHideout_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(TarkovApplication.HideoutController).GetMethod(
            nameof(TarkovApplication.HideoutController.UnloadHideout));
    }

    [PatchPrefix]
    public static void Prefix()
    {
        FikaHideoutCoop.OnHideoutUnloaded();
    }
}
