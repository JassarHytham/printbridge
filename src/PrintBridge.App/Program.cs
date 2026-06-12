using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using PrintBridge.Spooler;

namespace PrintBridge.App
{
    internal static class Program
    {
        private static Mutex _singleInstance;

        [STAThread]
        private static void Main(string[] args)
        {
            // Per-job capture process spawned by the port monitor: read stdin, pipe, exit.
            if (args.Length > 0 && args[0] == "--capture")
            {
                try { CaptureMode.Run(args); }
                catch { /* a lost job must not pop UI from a spooler child */ }
                return;
            }

            // Only one GUI instance (it owns the capture named pipe + listeners).
            _singleInstance = new Mutex(true, "PrintBridge.SingleInstance", out var isNew);
            if (!isNew)
            {
                MessageBox.Show("PrintBridge is already running (check the system tray).",
                    "PrintBridge");
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var configPath = AppConfig.DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(configPath));
            var config = AppConfig.Load(configPath);

            // Ghostscript path resolved relative to the install dir (Phase 5 bundles it).
            var gsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "ghostscript", "bin", "gswin64c.exe");
            IPrinterService printers = File.Exists(gsPath)
                ? (IPrinterService)new GhostscriptPrinter(gsPath)
                : new EnumOnlyPrinterService(); // graceful pre-bundle fallback

            var pcId = MachineIdentity.GetOrCreatePcId();
            var pcName = Environment.MachineName;

            var sharing = new SharingService(printers, () => config,
                () => config.SharedPrinters, pcId, pcName);
            var discovery = new DiscoveryService();
            var capture = new CaptureServer(() => config, discovery);

            sharing.Start();
            discovery.Start();
            capture.Start();

            using (sharing)
            using (discovery)
            using (capture)
            {
                Application.Run(new MainForm(printers, discovery, sharing, capture, config, configPath));
            }
        }
    }
}
