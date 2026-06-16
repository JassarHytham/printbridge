using System.Collections.Generic;
using Newtonsoft.Json;

namespace PrintBridge.Protocol
{
    /// <summary>
    /// A named paper format the user can pick at print time (e.g. "A4", "Receipt 80mm").
    /// Width/Height are in PostScript points (1/72"). Zero on both means "use the
    /// printer's own default media" — nothing is forced.
    /// </summary>
    public class PrintFormat
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("widthPoints")]
        public double WidthPoints { get; set; }

        [JsonProperty("heightPoints")]
        public double HeightPoints { get; set; }

        /// <summary>Scale the document to fit the media (useful for receipts).</summary>
        [JsonProperty("fitToPage")]
        public bool FitToPage { get; set; }

        /// <summary>
        /// Read the media size from the document itself (its PostScript page setup)
        /// rather than forcing a fixed size. Honours ERP/templated layouts as authored.
        /// </summary>
        [JsonProperty("useDocumentSize")]
        public bool UseDocumentSize { get; set; }

        /// <summary>True when this format pins a specific media size.</summary>
        [JsonIgnore]
        public bool ForcesMedia => WidthPoints > 0 && HeightPoints > 0;

        public override string ToString() => Name;

        /// <summary>Millimetres → PostScript points, for readable definitions below.</summary>
        public static double Mm(double mm) => mm * 72.0 / 25.4;

        /// <summary>The formats every install ships with. Users can add more in the app.</summary>
        public static List<PrintFormat> BuiltIns() => new List<PrintFormat>
        {
            new PrintFormat { Name = "Auto (match document)", UseDocumentSize = true },     // read size from the doc
            new PrintFormat { Name = "Printer default" },                                   // 0,0 -> no forcing
            new PrintFormat { Name = "A4",           WidthPoints = Mm(210), HeightPoints = Mm(297) },
            new PrintFormat { Name = "Letter",       WidthPoints = 612,     HeightPoints = 792 },
            new PrintFormat { Name = "A5",           WidthPoints = Mm(148), HeightPoints = Mm(210) },
            new PrintFormat { Name = "Receipt 80mm", WidthPoints = Mm(80),  HeightPoints = Mm(297), FitToPage = true },
            new PrintFormat { Name = "Receipt 58mm", WidthPoints = Mm(58),  HeightPoints = Mm(210), FitToPage = true },
        };
    }
}
