using Newtonsoft.Json;

namespace Fika.Core.Networking.Models.Hideout;

public class FikaHideoutViewRequest
{
    [JsonProperty("accountId")]
    public string AccountId;
}
