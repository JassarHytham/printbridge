using System.Collections.Generic;
using Newtonsoft.Json;

namespace PrintBridge.Protocol
{
    /// <summary>
    /// Broadcast by every PC that has at least one shared printer, so others can
    /// discover it without manual IP entry. "Magic" guards against unrelated UDP
    /// traffic on the same port.
    /// </summary>
    public class DiscoveryBeacon
    {
        public const string ExpectedMagic = "PRINTBRIDGE/1";

        [JsonProperty("magic")]
        public string Magic { get; set; } = ExpectedMagic;

        [JsonProperty("appVersion")]
        public string AppVersion { get; set; }

        [JsonProperty("pcName")]
        public string PcName { get; set; }

        [JsonProperty("pcId")]
        public string PcId { get; set; }

        [JsonProperty("jobPort")]
        public int JobPort { get; set; }

        [JsonProperty("sharedPrinters")]
        public List<string> SharedPrinters { get; set; } = new List<string>();

        public string ToJson() => Json.Serialize(this);

        public static DiscoveryBeacon FromJson(string json)
        {
            try
            {
                var beacon = Json.Deserialize<DiscoveryBeacon>(json);
                if (beacon == null || beacon.Magic != ExpectedMagic) return null;
                return beacon;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
