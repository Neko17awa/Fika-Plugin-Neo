using EFT.Communications;
using Fika.Core.UI;
using Newtonsoft.Json;

namespace Fika.Core.Networking.Websocket.Notifications;

/// <summary>
/// NekoPT 组队邀请载荷类型。仅作扩展数据面，不改 Fika 好友/在线列表逻辑。
/// </summary>
public class PartyInviteReceivedNotification : Notification
{
    public override ENotificationIconType Icon => ENotificationIconType.Friend;

    public override string Description
    {
        get
        {
            var leaderText = FikaUIGlobals.ColorizeText(FikaUIGlobals.EColor.GREEN, LeaderNickname ?? string.Empty);
            return $"{leaderText} invited you to join their party";
        }
    }

    [JsonProperty("inviteId")]
    public string InviteId;

    [JsonProperty("partyId")]
    public string PartyId;

    [JsonProperty("leaderProfileId")]
    public string LeaderProfileId;

    [JsonProperty("leaderNickname")]
    public string LeaderNickname;

    [JsonProperty("targetProfileId")]
    public string TargetProfileId;

    [JsonProperty("targetNickname")]
    public string TargetNickname;

    [JsonProperty("expiresAtUnix")]
    public long ExpiresAtUnix;
}
