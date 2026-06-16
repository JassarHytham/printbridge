using System.Text;
using PrintBridge.Spooler;
using Xunit;

public class PostScriptPageSizeTests
{
    private static byte[] Ps(string s) => Encoding.ASCII.GetBytes(s);

    [Fact]
    public void TryParse_ReadsPageSizeFromSetPageDevice()
    {
        var ps = Ps("%!PS-Adobe-3.0\n<< /PageSize [595 842] >> setpagedevice\nshowpage\n");

        Assert.True(PostScriptPageSize.TryParse(ps, out var w, out var h));
        Assert.Equal(595, w);
        Assert.Equal(842, h);
    }

    [Fact]
    public void TryParse_PrefersPageSizeOverDocumentMedia()
    {
        // DocumentMedia says Letter, but the actual page request is a receipt roll.
        var ps = Ps("%%DocumentMedia: Letter 612 792 0 () ()\n"
                  + "<< /PageSize [226.77 841.89] >> setpagedevice\n");

        Assert.True(PostScriptPageSize.TryParse(ps, out var w, out var h));
        Assert.Equal(226.77, w, 2);
        Assert.Equal(841.89, h, 2);
    }

    [Fact]
    public void TryParse_FallsBackToDocumentMedia()
    {
        var ps = Ps("%!PS-Adobe-3.0\n%%DocumentMedia: A4 595 842 80 white ()\n%%EndComments\n");

        Assert.True(PostScriptPageSize.TryParse(ps, out var w, out var h));
        Assert.Equal(595, w);
        Assert.Equal(842, h);
    }

    [Fact]
    public void TryParse_FallsBackToBoundingBox()
    {
        var ps = Ps("%!PS-Adobe-3.0 EPSF-3.0\n%%BoundingBox: 0 0 200 400\n");

        Assert.True(PostScriptPageSize.TryParse(ps, out var w, out var h));
        Assert.Equal(200, w);
        Assert.Equal(400, h);
    }

    [Fact]
    public void TryParse_ReturnsFalseWhenNothingDeclared()
    {
        var ps = Ps("%!PS-Adobe-3.0\nshowpage\n");

        Assert.False(PostScriptPageSize.TryParse(ps, out var w, out var h));
        Assert.Equal(0, w);
        Assert.Equal(0, h);
    }

    [Fact]
    public void TryParse_HandlesNullAndEmpty()
    {
        Assert.False(PostScriptPageSize.TryParse(null, out _, out _));
        Assert.False(PostScriptPageSize.TryParse(new byte[0], out _, out _));
    }

    [Fact]
    public void TryParse_IgnoresAtEndBoundingBox()
    {
        // "%%BoundingBox: (atend)" must not match; nothing else declared -> false.
        var ps = Ps("%!PS-Adobe-3.0\n%%BoundingBox: (atend)\nshowpage\n");

        Assert.False(PostScriptPageSize.TryParse(ps, out _, out _));
    }
}
