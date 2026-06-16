using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PrintBridge.Spooler
{
    /// <summary>
    /// Extracts the page/media size (PostScript points, 1/72") that a document was
    /// authored for, by reading the DSC headers and page-setup the Windows PostScript
    /// driver (or an ERP template) emits. This lets PrintBridge honour a document's own
    /// geometry instead of forcing a fixed format the user happened to pick.
    /// </summary>
    public static class PostScriptPageSize
    {
        // Only the prolog and first-page setup need scanning; cap so a large binary
        // payload is never decoded into a string.
        private const int ScanLimitBytes = 2 * 1024 * 1024;

        // << ... /PageSize [ 595 842 ] ... >> setpagedevice — the literal render request.
        private static readonly Regex PageSizeRx = new Regex(
            @"/PageSize\s*\[\s*([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s*\]",
            RegexOptions.Compiled);

        // %%DocumentMedia: tag width height weight color type
        private static readonly Regex DocMediaRx = new Regex(
            @"%%DocumentMedia:\s*\S+\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)",
            RegexOptions.Compiled);

        // %%BoundingBox: llx lly urx ury — last resort (EPS content bounds).
        private static readonly Regex BBoxRx = new Regex(
            @"%%BoundingBox:\s*(-?[0-9]+(?:\.[0-9]+)?)\s+(-?[0-9]+(?:\.[0-9]+)?)\s+(-?[0-9]+(?:\.[0-9]+)?)\s+(-?[0-9]+(?:\.[0-9]+)?)",
            RegexOptions.Compiled);

        /// <summary>
        /// Tries to read the intended media size from a PostScript stream. Returns false
        /// (with zero dimensions) when nothing usable is found, so callers can fall back
        /// to the printer's own default media.
        /// </summary>
        public static bool TryParse(byte[] postScript, out double widthPoints, out double heightPoints)
        {
            widthPoints = 0;
            heightPoints = 0;
            if (postScript == null || postScript.Length == 0) return false;

            int len = Math.Min(postScript.Length, ScanLimitBytes);
            // ISO-8859-1 maps every byte 1:1, so ASCII markers survive and nothing throws.
            string text = Encoding.GetEncoding("ISO-8859-1").GetString(postScript, 0, len);

            // Prefer the explicit per-page request, then document media, then EPS bbox.
            var m = PageSizeRx.Match(text);
            if (m.Success && ParsePair(m.Groups[1].Value, m.Groups[2].Value, out widthPoints, out heightPoints))
                return true;

            m = DocMediaRx.Match(text);
            if (m.Success && ParsePair(m.Groups[1].Value, m.Groups[2].Value, out widthPoints, out heightPoints))
                return true;

            m = BBoxRx.Match(text);
            if (m.Success
                && TryNum(m.Groups[1].Value, out var llx) && TryNum(m.Groups[2].Value, out var lly)
                && TryNum(m.Groups[3].Value, out var urx) && TryNum(m.Groups[4].Value, out var ury))
            {
                widthPoints = urx - llx;
                heightPoints = ury - lly;
                if (widthPoints > 1 && heightPoints > 1) return true;
            }

            widthPoints = 0;
            heightPoints = 0;
            return false;
        }

        private static bool ParsePair(string w, string h, out double width, out double height)
        {
            width = 0; height = 0;
            return TryNum(w, out width) && TryNum(h, out height) && width > 1 && height > 1;
        }

        private static bool TryNum(string s, out double value) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
