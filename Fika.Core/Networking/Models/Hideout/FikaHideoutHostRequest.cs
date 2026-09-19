using Newtonsoft.Json;

namespace Fika.Core.Networking.Models.Hideout;

public class FikaHideoutHostRequest
{
    [JsonProperty("accountId")]
    public string AccountId;

    [JsonProperty("aliases")]
    public string[] Aliases;

    [JsonProperty("ips")]
    public string[] Ips;

    [JsonProperty("port")]
    public ushort Port;

    [JsonProperty("serverGuid")]
    public string ServerGuid;

    [JsonProperty("natPunch")]
    public bool NatPunch;

    [JsonProperty("useFikaNatPunchServer")]
    public bool UseFikaNatPunchServer;
}
