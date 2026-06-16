using System.Collections.Generic;
using PrintBridge.Spooler;

namespace PrintBridge.App
{
    /// <summary>Fallback used before Ghostscript is bundled: lists printers, can't print.</summary>
    public class EnumOnlyPrinterService : IPrinterService
    {
        private readonly PrinterEnumerator _enum = new PrinterEnumerator();
        public IReadOnlyList<string> ListLocalPrinters() => _enum.ListLocalPrinters();
        public void PrintPostScript(string printerName, string postScriptPath, int copies,
            double mediaWidthPoints, double mediaHeightPoints, bool fitToPage) =>
            throw new System.InvalidOperationException(
                "Ghostscript is not installed yet — run the full installer (Phase 5).");
    }
}
