using PrintBridge.Protocol;
using Xunit;

public class JobHeaderTests
{
    [Fact]
    public void ToJson_FromJson_RoundTrips()
    {
        var header = new JobHeader
        {
            JobId = "job-123",
            TargetPrinter = "HP LaserJet",
            Copies = 2,
            PaperSize = "A4",
            RequestingUser = "jassar",
            RequestingPc = "LAPTOP-1",
            Pin = "4242"
        };

        var back = JobHeader.FromJson(header.ToJson());

        Assert.Equal("job-123", back.JobId);
        Assert.Equal("HP LaserJet", back.TargetPrinter);
        Assert.Equal(2, back.Copies);
        Assert.Equal("A4", back.PaperSize);
        Assert.Equal("jassar", back.RequestingUser);
        Assert.Equal("LAPTOP-1", back.RequestingPc);
        Assert.Equal("4242", back.Pin);
    }

    [Fact]
    public void Copies_DefaultsToOne()
    {
        var header = new JobHeader();
        Assert.Equal(1, header.Copies);
    }
}
