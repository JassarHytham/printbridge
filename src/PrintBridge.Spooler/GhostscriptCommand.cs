using System.Text;

namespace PrintBridge.Spooler
{
    /// <summary>
    /// Builds the Ghostscript argument string that prints a PostScript file to a
    /// named Windows printer using the mswinpr2 device. This is the CutePDF/RedMon
    /// chain run in reverse (PostScript -> physical printer).
    /// </summary>
    public static class GhostscriptCommand
    {
        public static string BuildArguments(string printerName, string postScriptPath, int copies)
        {
            var sb = new StringBuilder();
            sb.Append("-dPrinted -dBATCH -dNOPAUSE -dNOSAFER -q ");
            if (copies > 1) sb.Append($"-dNumCopies={copies} ");
            sb.Append("-sDEVICE=mswinpr2 ");
            sb.Append($"-sOutputFile=\"%printer%{printerName}\" ");
            sb.Append($"\"{postScriptPath}\"");
            return sb.ToString();
        }
    }
}
