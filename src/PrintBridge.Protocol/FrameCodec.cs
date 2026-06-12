using System;
using System.IO;

namespace PrintBridge.Protocol
{
    /// <summary>
    /// Length-prefixed framing: a 4-byte big-endian unsigned length, then that
    /// many payload bytes. Used for both the JSON header and the PostScript body.
    /// </summary>
    public static class FrameCodec
    {
        // 200 MB cap — generous for print spool data, guards against bad input.
        public const int MaxFrameBytes = 200 * 1024 * 1024;

        public static void WriteFrame(Stream stream, byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Length > MaxFrameBytes)
                throw new ArgumentOutOfRangeException(nameof(payload),
                    $"Frame of {payload.Length} bytes exceeds max {MaxFrameBytes}.");

            var len = payload.Length;
            var header = new byte[4];
            header[0] = (byte)((len >> 24) & 0xFF);
            header[1] = (byte)((len >> 16) & 0xFF);
            header[2] = (byte)((len >> 8) & 0xFF);
            header[3] = (byte)(len & 0xFF);
            stream.Write(header, 0, 4);
            stream.Write(payload, 0, payload.Length);
        }

        public static byte[] ReadFrame(Stream stream)
        {
            var header = ReadExactly(stream, 4);
            int len = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            if (len < 0 || len > MaxFrameBytes)
                throw new InvalidDataException($"Declared frame length {len} is invalid.");
            return ReadExactly(stream, len);
        }

        private static byte[] ReadExactly(Stream stream, int count)
        {
            var buffer = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read == 0)
                    throw new EndOfStreamException(
                        $"Stream ended after {offset} of {count} bytes.");
                offset += read;
            }
            return buffer;
        }
    }
}
