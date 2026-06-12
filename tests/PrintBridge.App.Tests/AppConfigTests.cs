using System.IO;
using PrintBridge.App;
using Xunit;

public class AppConfigTests
{
    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        try
        {
            var cfg = new AppConfig
            {
                SharedPrinters = { "HP LaserJet" },
                Pin = "4242",
                ActiveRemotePcId = "pc1",
                ActiveRemotePrinter = "Canon MX"
            };
            cfg.Save(path);
            var loaded = AppConfig.Load(path);
            Assert.Contains("HP LaserJet", loaded.SharedPrinters);
            Assert.Equal("4242", loaded.Pin);
            Assert.Equal("pc1", loaded.ActiveRemotePcId);
            Assert.Equal("Canon MX", loaded.ActiveRemotePrinter);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Load_OnMissingFile_ReturnsDefaults()
    {
        var loaded = AppConfig.Load(Path.Combine(Path.GetTempPath(), "does-not-exist-xyz.json"));
        Assert.Empty(loaded.SharedPrinters);
        Assert.Null(loaded.Pin);
    }
}
