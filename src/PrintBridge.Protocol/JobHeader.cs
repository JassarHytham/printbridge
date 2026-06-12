using Newtonsoft.Json;

namespace PrintBridge.Protocol
{
    /// <summary>Metadata sent ahead of the PostScript payload for a print job.</summary>
    public class JobHeader
    {
        [JsonProperty("jobId")]
        public string JobId { get; set; }

        [JsonProperty("targetPrinter")]
        public string TargetPrinter { get; set; }

        [JsonProperty("copies")]
        public int Copies { get; set; } = 1;

        [JsonProperty("paperSize")]
        public string PaperSize { get; set; }

        [JsonProperty("requestingUser")]
        public string RequestingUser { get; set; }

        [JsonProperty("requestingPc")]
        public string RequestingPc { get; set; }

        [JsonProperty("pin")]
        public string Pin { get; set; }

        public string ToJson() => Json.Serialize(this);
        public static JobHeader FromJson(string json) => Json.Deserialize<JobHeader>(json);
    }
}
