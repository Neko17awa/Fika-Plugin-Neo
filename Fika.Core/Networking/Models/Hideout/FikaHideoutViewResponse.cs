using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Fika.Core.Networking.Models.Hideout;

/// <summary>
/// 本服藏身处参观快照。NekoPT 用 <c>FikaHideoutExt</c> 消费；Fika 自身不走这条路径。
/// </summary>
public class FikaHideoutViewResponse
{
    [JsonProperty("ok")]
    public bool Ok;

    [JsonProperty("aid")]
    public JToken Aid;

    [JsonProperty("info")]
    public JToken Info;

    [JsonProperty("hideout")]
    public JToken Hideout;

    [JsonProperty("hideoutAreaStashes")]
    public JToken HideoutAreaStashes;

    [JsonProperty("customizationStash")]
    public JToken CustomizationStash;

    [JsonProperty("items")]
    public JToken Items;
}
