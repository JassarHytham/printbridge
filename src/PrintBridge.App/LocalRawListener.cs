using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using PrintBridge.Protocol;

namespace PrintBridge.App
{
    /// <summary>
    /// Listens on 127.0.0.1:9100 (Standard TCP/IP RAW print port).
    /// The "Shared Printer (PrintBridge)" virtual printer's port points here.
    /// Each print job arrives as a raw PostScript stream; we forward it over
    /// the LAN to the selected remote PC via JobSender.
    ///
    /// No custom DLLs, no driver signing, no bcdedit test-signing mode.
    /// Works on x64, ARM64, and x86 — any Windows 7-11 architecture.
    /// </summary>
    public class LocalRawListener : IDisposable
    {
        public const int RawPort = 9100;

        private readonly Func<AppConfig> _getConfig;
        private readonly DiscoveryService _discovery;
        private readonly JobSender _sender = new JobSender();
        private TcpListener _listener;
        private CancellationTokenSource _cts;

        public event Action<string> JobLogged;

        public LocalRawListener(Func<AppConfig> getConfig, DiscoveryService discovery)
        {
            _getConfig = getConfig;
            _discovery = discovery;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, RawPort);
            _listener.Start();
            AcceptLoop();
        }

        private async void AcceptLoop()
        {
            while (_cts != null && !_cts.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(); }
                catch { break; }
                ThreadPool.QueueUserWorkItem(_ => HandleJob(client));
            }
        }

        private void HandleJob(TcpClient client)
        {
            using (client)
            {
                byte[] postScript;
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        client.GetStream().CopyTo(ms);
                        postScript = ms.ToArray();
                    }
                }
                catch (Exception ex)
                {
                    JobLogged?.Invoke($"Capture error: {ex.Message}");
                    return;
                }

                if (postScript.Length == 0) return;
                ForwardJob(postScript);
            }
        }

        private void ForwardJob(byte[] postScript)
        {
            var cfg = _getConfig();
            if (string.IsNullOrEmpty(cfg.ActiveRemotePrinter))
            {
                JobLogged?.Invoke("No remote printer selected — job dropped.");
                return;
            }

            var server = _discovery.Registry.GetActive(DateTime.UtcNow)
                .FirstOrDefault(s => s.PcId == cfg.ActiveRemotePcId);
            if (server == null)
            {
                JobLogged?.Invoke("Selected server is offline — job dropped.");
                return;
            }

            var header = new JobHeader
            {
                JobId        = Guid.NewGuid().ToString("N"),
                TargetPrinter = cfg.ActiveRemotePrinter,
                Copies       = 1,
                PaperSize    = "A4",
                RequestingUser = Environment.UserName,
                RequestingPc   = Environment.MachineName,
                Pin          = cfg.Pin
            };

            JobLogged?.Invoke($"Sending job to {server.PcName} -> {header.TargetPrinter}...");
            var result = _sender.Send(server.IpAddress, server.JobPort, header, postScript);
            JobLogged?.Invoke(result.Status == JobStatus.Printed
                ? $"Printed on {server.PcName}"
                : $"{result.Status}: {result.Detail}");
        }

        public void Dispose()
        {
            _cts?.Cancel();
            try { _listener?.Stop(); } catch { }
        }
    }
}
