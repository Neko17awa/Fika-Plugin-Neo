using System.Reflection;
using EFT;
using Fika.Core.Main.Components;
using SPT.Reflection.Patching;

namespace Fika.Core.Main.Patches.Hideout;

/// <summary>
/// SetGuest(true) 锁客人；SetGuest(false) 只有在不是参观时才确认为自己的藏身处。
/// </summary>
public class HideoutPlayerOwner_SetGuest_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(HideoutPlayerOwner).GetMethod(nameof(HideoutPlayerOwner.SetGuest));
    }

    [PatchPostfix]
    public static void Postfix(bool isGuest, string ownerAccountId)
    {
        FikaHideoutCoop.OnSetGuest(isGuest, ownerAccountId);
    }
}
