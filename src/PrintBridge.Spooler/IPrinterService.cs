using System.Collections.Generic;

namespace PrintBridge.Spooler
{
    /// <summary>Abstraction over Windows printing so the App can be tested with fakes.</summary>
    public interface IPrinterService
    {
        /// <summary>Names of printers installed on this PC.</summary>
        IReadOnlyList<string> ListLocalPrinters();

        /// <summary>
        /// Print a PostScript file to a named local printer. Throws on failure.
        /// When <paramref name="mediaWidthPoints"/> and <paramref name="mediaHeightPoints"/>
        /// are both &gt; 0 the media size is forced (e.g. receipt rolls); zeros mean
        /// "use the printer's default media". Dimensions are PostScript points (1/72").
        /// </summary>
        void PrintPostScript(string printerName, string postScriptPath, int copies,
            double mediaWidthPoints, double mediaHeightPoints, bool fitToPage);
    }
}
