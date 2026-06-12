using PrintBridge.Protocol;
using Xunit;

public class JobResultTests
{
    [Fact]
    public void RoundTrips_WithStatusAndDetail()
    {
        var result = JobResult.Rejected("authentication failed");
        var back = JobResult.FromJson(result.ToJson());
        Assert.Equal(JobStatus.Rejected, back.Status);
        Assert.Equal("authentication failed", back.Detail);
    }

    [Fact]
    public void Printed_HasPrintedStatus()
    {
        Assert.Equal(JobStatus.Printed, JobResult.Printed().Status);
    }

    [Fact]
    public void Accepted_HasAcceptedStatus()
    {
        Assert.Equal(JobStatus.Accepted, JobResult.Accepted().Status);
    }

    [Fact]
    public void Error_CarriesDetail()
    {
        var r = JobResult.Error("could not render document");
        Assert.Equal(JobStatus.Error, r.Status);
        Assert.Equal("could not render document", r.Detail);
    }
}
