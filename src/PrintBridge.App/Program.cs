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
            // Only one GUI instance may run (it owns port 9100 and port 49153).
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

            // Ghostscript resolved relative to install dir (bundled in third_party\ghostscript).
            var gsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "ghostscript", "bin", "gswin64c.exe");
            IPrinterService printers = File.Exists(gsPath)
                ? (IPrinterService)new GhostscriptPrinter(gsPath)
                : new EnumOnlyPrinterService();   // graceful pre-bundle fallback

            var pcId   = MachineIdentity.GetOrCreatePcId();
            var pcName = Environment.MachineName;

            var sharing     = new SharingService(printers, () => config,
                                  () => config.SharedPrinters, pcId, pcName);
            var discovery   = new DiscoveryService();
            var rawListener = new LocalRawListener(() => config, discovery);

            sharing.Start();
            discovery.Start();
            rawListener.Start();

            using (sharing)
            using (discovery)
            using (rawListener)
            {
                Application.Run(new MainForm(printers, discovery, sharing, rawListener, config, configPath));
            }
        }
    }
}
