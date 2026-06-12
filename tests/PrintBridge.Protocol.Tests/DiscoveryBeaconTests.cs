using System.Collections.Generic;
using PrintBridge.Protocol;
using Xunit;

public class DiscoveryBeaconTests
{
    [Fact]
    public void ToJson_FromJson_RoundTrips()
    {
        var beacon = new DiscoveryBeacon
        {
            AppVersion = "1.0.0",
            PcName = "OFFICE-PC",
            PcId = "11111111-1111-1111-1111-111111111111",
            JobPort = 49153,
            SharedPrinters = new List<string> { "HP LaserJet", "Canon MX" }
        };

        var json = beacon.ToJson();
        var back = DiscoveryBeacon.FromJson(json);

        Assert.Equal(beacon.PcName, back.PcName);
        Assert.Equal(beacon.PcId, back.PcId);
        Assert.Equal(beacon.JobPort, back.JobPort);
        Assert.Equal(beacon.SharedPrinters, back.SharedPrinters);
    }

    [Fact]
    public void FromJson_OnGarbage_ReturnsNull()
    {
        Assert.Null(DiscoveryBeacon.FromJson("not json at all"));
    }

    [Fact]
    public void FromJson_OnWrongMagic_ReturnsNull()
    {
        Assert.Null(DiscoveryBeacon.FromJson("{\"magic\":\"OTHER\"}"));
    }
}
