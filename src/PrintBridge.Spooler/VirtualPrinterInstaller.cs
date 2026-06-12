using System.Diagnostics;
using System.Drawing.Printing;
using System.Linq;

namespace PrintBridge.Spooler
{
    /// <summary>
    /// Manages the "Shared Printer (PrintBridge)" virtual printer. The active remote
    /// mapping lives in app config (read at send time), so switching targets needs no
    /// reconfiguration here — this only ensures the virtual printer exists at all.
    /// </summary>
    public class VirtualPrinterInstaller
    {
        public const string VirtualPrinterName = "Shared Printer (PrintBridge)";

        public bool IsInstalled() =>
            PrinterSettings.InstalledPrinters.Cast<string>().Contains(VirtualPrinterName);

        public void EnsureInstalled(string installScriptPath)
        {
            if (IsInstalled()) return;
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{installScriptPath}\"",
                Verb = "runas",          // elevation prompt
                UseShellExecute = true
            };
            Process.Start(psi)?.WaitForExit();
        }
    }
}
