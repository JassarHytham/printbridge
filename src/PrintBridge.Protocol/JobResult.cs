using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace PrintBridge.Protocol
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum JobStatus { Accepted, Rejected, Printed, Error }

    /// <summary>Server's response to a job: first Accepted/Rejected, then Printed/Error.</summary>
    public class JobResult
    {
        [JsonProperty("status")]
        public JobStatus Status { get; set; }

        [JsonProperty("detail")]
        public string Detail { get; set; }

        public static JobResult Accepted() => new JobResult { Status = JobStatus.Accepted };
        public static JobResult Rejected(string detail) =>
            new JobResult { Status = JobStatus.Rejected, Detail = detail };
        public static JobResult Printed() => new JobResult { Status = JobStatus.Printed };
        public static JobResult Error(string detail) =>
            new JobResult { Status = JobStatus.Error, Detail = detail };

        public string ToJson() => Json.Serialize(this);
        public static JobResult FromJson(string json) => Json.Deserialize<JobResult>(json);
    }
}
