using System.Reflection;
using EFT;
using Fika.Core.Main.Utils;
using SPT.Reflection.Patching;

namespace Fika.Core.Main.Patches.Hideout;

/// <summary>
/// 回到自己的藏身处时清掉参观目标，避免一直按客人去连上一次的 Host。
/// </summary>
public class HideoutPlayerOwner_SetGuest_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(HideoutPlayerOwner).GetMethod(nameof(HideoutPlayerOwner.SetGuest));
    }

    [PatchPostfix]
    public static void Postfix(bool isGuest)
    {
        if (!isGuest)
        {
            FikaBackendUtils.HideoutVisitInProgressId = string.Empty;
        }
    }
}
