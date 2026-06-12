using PrintBridge.Spooler;
using Xunit;

public class GhostscriptCommandTests
{
    [Fact]
    public void BuildArguments_TargetsNamedPrinterViaMswinpr2()
    {
        var args = GhostscriptCommand.BuildArguments(
            printerName: "HP LaserJet", postScriptPath: @"C:\temp\job.ps", copies: 1);

        Assert.Contains("-sDEVICE=mswinpr2", args);
        Assert.Contains("-sOutputFile=\"%printer%HP LaserJet\"", args);
        Assert.Contains("\"C:\\temp\\job.ps\"", args);
        Assert.Contains("-dBATCH", args);
        Assert.Contains("-dNOPAUSE", args);
    }

    [Fact]
    public void BuildArguments_IncludesCopiesWhenGreaterThanOne()
    {
        var args = GhostscriptCommand.BuildArguments("HP", @"C:\j.ps", copies: 3);
        Assert.Contains("-dNumCopies=3", args);
    }

    [Fact]
    public void BuildArguments_OmitsCopiesWhenOne()
    {
        var args = GhostscriptCommand.BuildArguments("HP", @"C:\j.ps", copies: 1);
        Assert.DoesNotContain("-dNumCopies", args);
    }
}
