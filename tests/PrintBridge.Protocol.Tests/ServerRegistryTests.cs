using System;
using System.Collections.Generic;
using System.Linq;
using PrintBridge.Protocol;
using Xunit;

public class ServerRegistryTests
{
    private static DiscoveryBeacon Beacon(string pcId, string pcName, params string[] printers)
        => new DiscoveryBeacon
        {
            PcId = pcId, PcName = pcName, JobPort = 49153,
            SharedPrinters = printers.ToList(), AppVersion = "1.0.0"
        };

    [Fact]
    public void Observe_AddsServer()
    {
        var reg = new ServerRegistry(ttl: TimeSpan.FromSeconds(10));
        var now = DateTime.UtcNow;
        reg.Observe(Beacon("pc1", "OFFICE", "HP"), "192.168.1.10", now);
        var active = reg.GetActive(now);
        Assert.Single(active);
        Assert.Equal("OFFICE", active[0].PcName);
        Assert.Equal("192.168.1.10", active[0].IpAddress);
        Assert.Equal(new List<string> { "HP" }, active[0].SharedPrinters);
    }

    [Fact]
    public void Observe_SamePcId_UpdatesNotDuplicates()
    {
        var reg = new ServerRegistry(ttl: TimeSpan.FromSeconds(10));
        var now = DateTime.UtcNow;
        reg.Observe(Beacon("pc1", "OFFICE", "HP"), "192.168.1.10", now);
        reg.Observe(Beacon("pc1", "OFFICE", "HP", "Canon"), "192.168.1.10", now.AddSeconds(2));
        var active = reg.GetActive(now.AddSeconds(2));
        Assert.Single(active);
        Assert.Equal(2, active[0].SharedPrinters.Count);
    }

    [Fact]
    public void GetActive_ExcludesExpiredServers()
    {
        var reg = new ServerRegistry(ttl: TimeSpan.FromSeconds(10));
        var t0 = DateTime.UtcNow;
        reg.Observe(Beacon("pc1", "OFFICE", "HP"), "192.168.1.10", t0);
        var later = t0.AddSeconds(11);
        Assert.Empty(reg.GetActive(later));
    }

    [Fact]
    public void GetActive_KeepsServerRefreshedWithinTtl()
    {
        var reg = new ServerRegistry(ttl: TimeSpan.FromSeconds(10));
        var t0 = DateTime.UtcNow;
        reg.Observe(Beacon("pc1", "OFFICE", "HP"), "192.168.1.10", t0);
        reg.Observe(Beacon("pc1", "OFFICE", "HP"), "192.168.1.10", t0.AddSeconds(8));
        Assert.Single(reg.GetActive(t0.AddSeconds(15)));
    }
}
