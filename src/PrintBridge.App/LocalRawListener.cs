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

        /// <summary>
        /// When set, called once per job to decide the target + settings (the GUI shows
        /// a picker on the UI thread). Returns null if the user cancelled. When unset,
        /// the job falls back to the remembered default in config.
        /// </summary>
        public Func<JobRouting> ResolveRouting { get; set; }

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

            ServerEntry server;
            string targetPrinter;
            int copies = 1;
            PrintFormat format = null;

            if (ResolveRouting != null)
            {
                // GUI present: ask the user (or auto-resolve the default) on the UI thread.
                var routing = ResolveRouting();
                if (routing == null)
                {
                    JobLogged?.Invoke("Print cancelled.");
                    return;
                }
                server        = routing.Server;
                targetPrinter = routing.PrinterName;
                copies        = Math.Max(1, routing.Copies);
                format        = routing.Format;
            }
            else
            {
                // Headless fallback: use the remembered default target.
                if (string.IsNullOrEmpty(cfg.ActiveRemotePrinter))
                {
                    JobLogged?.Invoke("No remote printer selected — job dropped.");
                    return;
                }
                server = _discovery.Registry.GetActive(DateTime.UtcNow)
                    .FirstOrDefault(s => s.PcId == cfg.ActiveRemotePcId);
                targetPrinter = cfg.ActiveRemotePrinter;
            }

            if (server == null)
            {
                JobLogged?.Invoke("Selected server is offline — job dropped.");
                return;
            }

            var header = new JobHeader
            {
                JobId          = Guid.NewGuid().ToString("N"),
                TargetPrinter  = targetPrinter,
                Copies         = copies,
                PaperSize      = format?.Name ?? "Printer default",
                MediaWidthPoints  = format?.WidthPoints ?? 0,
                MediaHeightPoints = format?.HeightPoints ?? 0,
                FitToPage      = format?.FitToPage ?? false,
                RequestingUser = Environment.UserName,
                RequestingPc   = Environment.MachineName,
                Pin            = cfg.Pin
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
