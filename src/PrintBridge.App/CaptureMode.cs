using System;
using System.IO;
using System.IO.Pipes;
using PrintBridge.Protocol;

namespace PrintBridge.App
{
    /// <summary>
    /// Runs when the port monitor spawns us per job. Reads the spooled PostScript
    /// from stdin and forwards it to the already-running GUI app over a named pipe.
    /// </summary>
    public static class CaptureMode
    {
        public static void Run(string[] args)
        {
            using (var stdin = Console.OpenStandardInput())
            using (var buffer = new MemoryStream())
            {
                stdin.CopyTo(buffer);
                var ps = buffer.ToArray();

                using (var pipe = new NamedPipeClientStream(".", CaptureServer.PipeName,
                    PipeDirection.Out))
                {
                    pipe.Connect(5000);
                    FrameCodec.WriteFrame(pipe, ps);
                    pipe.Flush();
                }
            }
        }
    }
}
