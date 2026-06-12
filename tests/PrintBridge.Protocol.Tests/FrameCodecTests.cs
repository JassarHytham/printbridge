using System;
using System.IO;
using System.Text;
using PrintBridge.Protocol;
using Xunit;

public class FrameCodecTests
{
    [Fact]
    public void WriteThenRead_ReturnsSamePayload()
    {
        var payload = Encoding.UTF8.GetBytes("hello frame");
        using var ms = new MemoryStream();
        FrameCodec.WriteFrame(ms, payload);
        ms.Position = 0;
        var read = FrameCodec.ReadFrame(ms);
        Assert.Equal(payload, read);
    }

    [Fact]
    public void WriteThenRead_TwoFrames_AreReadInOrder()
    {
        var a = Encoding.UTF8.GetBytes("first");
        var b = Encoding.UTF8.GetBytes("second");
        using var ms = new MemoryStream();
        FrameCodec.WriteFrame(ms, a);
        FrameCodec.WriteFrame(ms, b);
        ms.Position = 0;
        Assert.Equal(a, FrameCodec.ReadFrame(ms));
        Assert.Equal(b, FrameCodec.ReadFrame(ms));
    }

    [Fact]
    public void ReadFrame_OnEmptyStream_ThrowsEndOfStream()
    {
        using var ms = new MemoryStream();
        Assert.Throws<EndOfStreamException>(() => FrameCodec.ReadFrame(ms));
    }

    [Fact]
    public void WriteFrame_RejectsOversizedPayload()
    {
        using var ms = new MemoryStream();
        var tooBig = FrameCodec.MaxFrameBytes + 1;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FrameCodec.WriteFrame(ms, new byte[tooBig]));
    }
}
