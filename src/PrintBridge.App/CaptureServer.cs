using System;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using PrintBridge.Protocol;

namespace PrintBridge.App
{
    /// <summary>
    /// Receives spooled PostScript from the per-job "--capture" child process over a
    /// named pipe, builds a JobHeader from the active config, resolves the selected
    /// server's IP from discovery, and ships the job via JobSender.
    /// </summary>
    public class CaptureServer : IDisposable
    {
        public const string PipeName = "PrintBridge.Capture";
        private readonly Func<AppConfig> _config;
        private readonly DiscoveryService _discovery;
        private readonly JobSender _sender = new JobSender();
        private CancellationTokenSource _cts;
        public event Action<string> JobLogged;

        public CaptureServer(Func<AppConfig> config, DiscoveryService discovery)
        { _config = config; _discovery = discovery; }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            new Thread(Loop) { IsBackground = true }.Start();
        }

        private void Loop()
        {
            while (_cts != null && !_cts.IsCancellationRequested)
            {
                using (var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                {
                    try { pipe.WaitForConnection(); } catch { break; }
                    try
                    {
                        var ps = FrameCodec.ReadFrame(pipe);
                        ForwardJob(ps);
                    }
                    catch (Exception ex) { JobLogged?.Invoke($"Capture error: {ex.Message}"); }
                }
            }
        }

        private void ForwardJob(byte[] postScript)
        {
            var cfg = _config();
            if (string.IsNullOrEmpty(cfg.ActiveRemotePrinter))
            { JobLogged?.Invoke("No remote printer selected — job dropped."); return; }

            var server = _discovery.Registry.GetActive(DateTime.UtcNow)
                .FirstOrDefault(s => s.PcId == cfg.ActiveRemotePcId);
            if (server == null)
            { JobLogged?.Invoke("Selected server is offline — job dropped."); return; }

            var header = new JobHeader
            {
                JobId = Guid.NewGuid().ToString(),
                TargetPrinter = cfg.ActiveRemotePrinter,
                Copies = 1,
                PaperSize = "A4",
                RequestingUser = Environment.UserName,
                RequestingPc = Environment.MachineName,
                Pin = cfg.Pin
            };
            var result = _sender.Send(server.IpAddress, server.JobPort, header, postScript);
            JobLogged?.Invoke($"Sent job to {server.PcName}: {result.Status} {result.Detail}");
        }

        public void Dispose() { _cts?.Cancel(); }
    }
}
