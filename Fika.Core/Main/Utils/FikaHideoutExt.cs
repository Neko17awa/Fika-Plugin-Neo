using System;
using Comfort.Common;
using EFT;
using Fika.Core.Networking.Http;
using Fika.Core.Networking.Models.Hideout;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Fika.Core.Main.Utils;

/// <summary>
/// 客人藏身处进场挂点。Fika 自身不调用；NekoPT HideoutEX 用本服快照或已构造的 <see cref="HideoutData"/> 进场。
/// </summary>
public static class FikaHideoutExt
{
    /// <summary>
    /// 以客人身份进入给定藏身处快照。要求 <see cref="HideoutData.IsGuest"/> 为 true。
    /// </summary>
    public static void VisitGuestHideout(HideoutData hideoutData)
    {
        if (hideoutData == null || !hideoutData.IsGuest)
        {
            FikaGlobals.LogWarning("FikaHideoutExt.VisitGuestHideout ignored: missing guest HideoutData");
            return;
        }

        var app = Singleton<ClientApplication<IEftSession>>.Instance as TarkovApplication;
        if (app?.HideoutControllerAccess == null)
        {
            FikaGlobals.LogWarning("FikaHideoutExt.VisitGuestHideout ignored: HideoutController not ready");
            return;
        }

        FikaBackendUtils.HideoutVisitInProgressId = hideoutData.OwnerAccountId;
        app.HideoutControllerAccess.HideoutSelectedHandler(hideoutData).HandleExceptions();
    }

    /// <summary>
    /// 向本服 <c>/fika/hideout/view</c> 取快照并进场。找不到目标返回 false，不会进查看者自己的藏身处。
    /// </summary>
    public static bool TryVisitAccount(string accountId)
    {
        if (string.IsNullOrEmpty(accountId))
        {
            return false;
        }

        FikaHideoutViewResponse response;
        try
        {
            response = FikaRequestHandler.GetHideoutView(new FikaHideoutViewRequest { AccountId = accountId });
        }
        catch (Exception ex)
        {
            FikaGlobals.LogWarning($"FikaHideoutExt.TryVisitAccount failed: {ex.Message}");
            return false;
        }

        return TryVisitFromView(response);
    }

    public static bool TryVisitFromView(FikaHideoutViewResponse response)
    {
        if (response == null || !response.Ok || response.Hideout == null || response.Hideout.Type == JTokenType.Null)
        {
            return false;
        }

        try
        {
            var payload = new JObject
            {
                ["aid"] = response.Aid,
                ["info"] = response.Info,
                ["hideout"] = response.Hideout,
                ["hideoutAreaStashes"] = response.HideoutAreaStashes,
                ["customizationStash"] = response.CustomizationStash,
                ["items"] = response.Items
            };
            var descriptor = JsonConvert.DeserializeObject<OtherPlayerProfileDescriptor>(payload.ToString());
            if (descriptor?.HideoutData == null)
            {
                return false;
            }

            VisitGuestHideout(descriptor.HideoutData);
            return true;
        }
        catch (Exception ex)
        {
            FikaGlobals.LogWarning($"FikaHideoutExt.TryVisitFromView failed: {ex.Message}");
            return false;
        }
    }
}
