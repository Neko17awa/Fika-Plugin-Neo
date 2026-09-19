using EFT.Communications;
using Fika.Core.UI;
using Newtonsoft.Json;

namespace Fika.Core.Networking.Websocket.Notifications;

/// <summary>
/// NekoPT MixedSide 反射创建并写入 <see cref="Fika.Core.Main.Utils.FikaBackendUtils.PendingAcceptedRaidInvite"/>。
/// 不参与 Fika 原有开局流程。
/// </summary>
public class RaidInviteReceivedNotification : Notification
{
    public override ENotificationIconType Icon => ENotificationIconType.EntryPoint;

    public override string Description
    {
        get
        {
            var leaderText = FikaUIGlobals.ColorizeText(FikaUIGlobals.EColor.GREEN, LeaderNickname ?? string.Empty);
            return $"{leaderText} invited you to join raid at {(Location ?? string.Empty).Localized()}";
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

    [JsonProperty("serverId")]
    public string ServerId;

    [JsonProperty("raidCode")]
    public string RaidCode;

    [JsonProperty("location")]
    public string Location;

    [JsonProperty("side")]
    public int Side;

    [JsonProperty("time")]
    public int Time;

    [JsonProperty("expiresAtUnix")]
    public long ExpiresAtUnix;
}
