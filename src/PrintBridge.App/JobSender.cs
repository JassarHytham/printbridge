using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using PrintBridge.Protocol;

namespace PrintBridge.App
{
    public class JobSender
    {
        /// <summary>
        /// Sends a job to a server and returns the final result. Reads two result
        /// frames: the initial Accepted/Rejected, then the terminal Printed/Error.
        /// </summary>
        public JobResult Send(string serverIp, int jobPort, JobHeader header, byte[] postScript,
            int timeoutMs = 30000)
        {
            using (var client = new TcpClient())
            {
                var connect = client.BeginConnect(serverIp, jobPort, null, null);
                if (!connect.AsyncWaitHandle.WaitOne(timeoutMs))
                    return JobResult.Error("server offline");
                client.EndConnect(connect);

                using (var stream = client.GetStream())
                {
                    FrameCodec.WriteFrame(stream, Encoding.UTF8.GetBytes(header.ToJson()));
                    FrameCodec.WriteFrame(stream, postScript);
                    stream.Flush();

                    var first = JobResult.FromJson(
                        Encoding.UTF8.GetString(FrameCodec.ReadFrame(stream)));
                    if (first.Status == JobStatus.Rejected) return first;

                    try
                    {
                        return JobResult.FromJson(
                            Encoding.UTF8.GetString(FrameCodec.ReadFrame(stream)));
                    }
                    catch (IOException) { return JobResult.Error("connection lost during print"); }
                }
            }
        }
    }
}
