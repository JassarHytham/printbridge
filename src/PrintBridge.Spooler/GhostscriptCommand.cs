using System.Globalization;
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
            => BuildArguments(printerName, postScriptPath, copies, 0, 0, false);

        /// <summary>
        /// Full overload. When <paramref name="mediaWidthPoints"/> and
        /// <paramref name="mediaHeightPoints"/> are both &gt; 0, the media size is forced
        /// (e.g. for receipt rolls); <paramref name="fitToPage"/> then scales the document
        /// to fit that media. Dimensions are PostScript points (1/72").
        /// </summary>
        public static string BuildArguments(string printerName, string postScriptPath, int copies,
            double mediaWidthPoints, double mediaHeightPoints, bool fitToPage)
        {
            var sb = new StringBuilder();
            sb.Append("-dPrinted -dBATCH -dNOPAUSE -dNOSAFER -q ");
            if (copies > 1) sb.Append($"-dNumCopies={copies} ");

            if (mediaWidthPoints > 0 && mediaHeightPoints > 0)
            {
                sb.Append($"-dDEVICEWIDTHPOINTS={Num(mediaWidthPoints)} ");
                sb.Append($"-dDEVICEHEIGHTPOINTS={Num(mediaHeightPoints)} ");
                sb.Append("-dFIXEDMEDIA ");
                if (fitToPage) sb.Append("-dFitPage ");
            }

            sb.Append("-sDEVICE=mswinpr2 ");
            sb.Append($"-sOutputFile=\"%printer%{printerName}\" ");
            sb.Append($"\"{postScriptPath}\"");
            return sb.ToString();
        }

        // Invariant formatting so locales with a comma decimal separator don't break the args.
        private static string Num(double value) =>
            value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
