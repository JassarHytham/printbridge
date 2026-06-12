using System.Collections.Generic;

namespace PrintBridge.Spooler
{
    /// <summary>Abstraction over Windows printing so the App can be tested with fakes.</summary>
    public interface IPrinterService
    {
        /// <summary>Names of printers installed on this PC.</summary>
        IReadOnlyList<string> ListLocalPrinters();

        /// <summary>Print a PostScript file to a named local printer. Throws on failure.</summary>
        void PrintPostScript(string printerName, string postScriptPath, int copies);
    }
}
