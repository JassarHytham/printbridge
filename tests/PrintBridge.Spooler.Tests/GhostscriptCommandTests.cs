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

    [Fact]
    public void BuildArguments_ForcesMediaWhenWidthAndHeightGiven()
    {
        var args = GhostscriptCommand.BuildArguments("HP", @"C:\j.ps", copies: 1,
            mediaWidthPoints: 226.77, mediaHeightPoints: 841.89, fitToPage: true);

        Assert.Contains("-dDEVICEWIDTHPOINTS=226.77", args);
        Assert.Contains("-dDEVICEHEIGHTPOINTS=841.89", args);
        Assert.Contains("-dFIXEDMEDIA", args);
        Assert.Contains("-dFitPage", args);
    }

    [Fact]
    public void BuildArguments_OmitsMediaWhenDimensionsZero()
    {
        var args = GhostscriptCommand.BuildArguments("HP", @"C:\j.ps", copies: 1,
            mediaWidthPoints: 0, mediaHeightPoints: 0, fitToPage: false);

        Assert.DoesNotContain("-dDEVICEWIDTHPOINTS", args);
        Assert.DoesNotContain("-dFIXEDMEDIA", args);
    }
}
