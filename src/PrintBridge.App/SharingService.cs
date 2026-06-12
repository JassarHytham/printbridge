using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using PrintBridge.Protocol;
using PrintBridge.Spooler;

namespace PrintBridge.App
{
    /// <summary>
    /// The "sharing" half of the app: broadcasts a discovery beacon listing shared
    /// printers, and accepts incoming print jobs, printing them via IPrinterService.
    /// </summary>
    public class SharingService : IDisposable
    {
        public const int DiscoveryPort = 49152;
        public const int DefaultJobPort = 49153;

        private readonly IPrinterService _printers;
        private readonly Func<AppConfig> _config;
        private readonly Func<IReadOnlyList<string>> _sharedPrinters;
        private readonly int _jobPort;
        private UdpClient _beaconSocket;
        private TcpListener _jobListener;
        private Timer _beaconTimer;
        private CancellationTokenSource _cts;
        private readonly string _pcId;
        private readonly string _pcName;

        public event Action<string> JobLogged;   // human-readable log lines for the GUI

        public SharingService(IPrinterService printers, Func<AppConfig> config,
            Func<IReadOnlyList<string>> sharedPrinters, string pcId, string pcName,
            int jobPort = DefaultJobPort)
        {
            _printers = printers;
            _config = config;
            _sharedPrinters = sharedPrinters;
            _pcId = pcId;
            _pcName = pcName;
            _jobPort = jobPort;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _beaconSocket = new UdpClient { EnableBroadcast = true };
            _beaconTimer = new Timer(_ => SendBeacon(), null, 0, 3000);

            _jobListener = new TcpListener(IPAddress.Any, _jobPort);
            _jobListener.Start();
            AcceptLoop();
        }

        private void SendBeacon()
        {
            try
            {
                var shared = _sharedPrinters();
                if (shared == null || shared.Count == 0) return; // only advertise if sharing
                var beacon = new DiscoveryBeacon
                {
                    AppVersion = "1.0.0",
                    PcId = _pcId,
                    PcName = _pcName,
                    JobPort = _jobPort,
                    SharedPrinters = new List<string>(shared)
                };
                var bytes = Encoding.UTF8.GetBytes(beacon.ToJson());
                _beaconSocket.Send(bytes, bytes.Length,
                    new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
            }
            catch (Exception ex) { JobLogged?.Invoke($"Beacon error: {ex.Message}"); }
        }

        private async void AcceptLoop()
        {
            while (_cts != null && !_cts.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _jobListener.AcceptTcpClientAsync(); }
                catch { break; }
                ThreadPool.QueueUserWorkItem(_ => HandleJob(client));
            }
        }

        private void HandleJob(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                string tempPs = null;
                try
                {
                    var headerJson = Encoding.UTF8.GetString(FrameCodec.ReadFrame(stream));
                    var header = JobHeader.FromJson(headerJson);
                    var payload = FrameCodec.ReadFrame(stream);

                    var cfg = _config();
                    if (!string.IsNullOrEmpty(cfg.Pin) && cfg.Pin != header.Pin)
                    {
                        WriteResult(stream, JobResult.Rejected("authentication failed"));
                        JobLogged?.Invoke($"Rejected job from {header.RequestingPc}: bad PIN");
                        return;
                    }
                    if (!IsShared(header.TargetPrinter))
                    {
                        WriteResult(stream, JobResult.Rejected("printer not shared"));
                        return;
                    }

                    WriteResult(stream, JobResult.Accepted());

                    tempPs = Path.Combine(Path.GetTempPath(), $"pb_{header.JobId}.ps");
                    File.WriteAllBytes(tempPs, payload);
                    _printers.PrintPostScript(header.TargetPrinter, tempPs, Math.Max(1, header.Copies));

                    WriteResult(stream, JobResult.Printed());
                    JobLogged?.Invoke(
                        $"Printed job from {header.RequestingUser}@{header.RequestingPc} -> {header.TargetPrinter}");
                }
                catch (Exception ex)
                {
                    try { WriteResult(stream, JobResult.Error(ex.Message)); } catch { }
                    JobLogged?.Invoke($"Job error: {ex.Message}");
                }
                finally
                {
                    if (tempPs != null && File.Exists(tempPs))
                        try { File.Delete(tempPs); } catch { }
                }
            }
        }

        private bool IsShared(string printerName)
        {
            var shared = _sharedPrinters();
            return shared != null && printerName != null && shared.Contains(printerName);
        }

        private static void WriteResult(Stream stream, JobResult result)
        {
            var bytes = Encoding.UTF8.GetBytes(result.ToJson());
            FrameCodec.WriteFrame(stream, bytes);
            stream.Flush();
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _beaconTimer?.Dispose();
            try { _jobListener?.Stop(); } catch { }
            _beaconSocket?.Close();
        }
    }
}
