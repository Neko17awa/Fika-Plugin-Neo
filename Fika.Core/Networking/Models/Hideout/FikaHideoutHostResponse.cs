using System;
using Fika.Core.Networking.Models;
using Newtonsoft.Json;

namespace Fika.Core.Networking.Models.Hideout;

public class FikaHideoutHostResponse
{
    [JsonProperty("ok")]
    public bool Ok;

    [JsonProperty("ips")]
    public string[] Ips;

    [JsonProperty("serverGuid")]
    public Guid ServerGuid;

    [JsonProperty("port")]
    public ushort Port;

    [JsonProperty("natPunch")]
    public bool NatPunch;

    [JsonProperty("useFikaNatPunchServer")]
    public bool UseFikaNatPunchServer;

    [JsonProperty("isHeadless")]
    public bool IsHeadless;

    public GetHostResponse ToGetHostResponse()
    {
        return new GetHostResponse(Ips ?? [], ServerGuid, Port, NatPunch, UseFikaNatPunchServer, IsHeadless);
    }
}
