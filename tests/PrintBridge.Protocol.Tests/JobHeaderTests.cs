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
            PaperSize = "Receipt 80mm",
            MediaWidthPoints = 226.77,
            MediaHeightPoints = 841.89,
            FitToPage = true,
            RequestingUser = "jassar",
            RequestingPc = "LAPTOP-1",
            Pin = "4242"
        };

        var back = JobHeader.FromJson(header.ToJson());

        Assert.Equal("job-123", back.JobId);
        Assert.Equal("HP LaserJet", back.TargetPrinter);
        Assert.Equal(2, back.Copies);
        Assert.Equal("Receipt 80mm", back.PaperSize);
        Assert.Equal(226.77, back.MediaWidthPoints);
        Assert.Equal(841.89, back.MediaHeightPoints);
        Assert.True(back.FitToPage);
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
