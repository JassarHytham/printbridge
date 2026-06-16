using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace PrintBridge.Spooler
{
    /// <summary>
    /// Concrete IPrinterService. Enumerates printers and prints PostScript via the
    /// bundled Ghostscript console binary. The path to gs is injected so tests and
    /// installers can point at the bundled copy.
    /// </summary>
    public class GhostscriptPrinter : IPrinterService
    {
        private readonly string _ghostscriptExePath;
        private readonly PrinterEnumerator _enumerator = new PrinterEnumerator();

        public GhostscriptPrinter(string ghostscriptExePath)
        {
            if (!File.Exists(ghostscriptExePath))
                throw new FileNotFoundException("Ghostscript not found.", ghostscriptExePath);
            _ghostscriptExePath = ghostscriptExePath;
        }

        public IReadOnlyList<string> ListLocalPrinters() => _enumerator.ListLocalPrinters();

        public void PrintPostScript(string printerName, string postScriptPath, int copies,
            double mediaWidthPoints, double mediaHeightPoints, bool fitToPage)
        {
            if (!File.Exists(postScriptPath))
                throw new FileNotFoundException("PostScript file missing.", postScriptPath);

            var args = GhostscriptCommand.BuildArguments(printerName, postScriptPath, copies,
                mediaWidthPoints, mediaHeightPoints, fitToPage);
            var psi = new ProcessStartInfo
            {
                FileName = _ghostscriptExePath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using (var proc = Process.Start(psi))
            {
                var stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(120_000);
                if (!proc.HasExited)
                {
                    try { proc.Kill(); } catch { /* best effort */ }
                    throw new TimeoutException("Ghostscript timed out printing the job.");
                }
                if (proc.ExitCode != 0)
                    throw new InvalidOperationException(
                        $"Ghostscript failed (exit {proc.ExitCode}): {stderr}");
            }
        }
    }
}
