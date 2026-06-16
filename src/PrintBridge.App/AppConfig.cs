using System.Collections.Generic;
using System.IO;
using PrintBridge.Protocol;

namespace PrintBridge.App
{
    public class AppConfig
    {
        public List<string> SharedPrinters { get; set; } = new List<string>();
        public string Pin { get; set; }
        public string ActiveRemotePcId { get; set; }
        public string ActiveRemotePrinter { get; set; }

        /// <summary>Pop the picker dialog before every job (default). When false, the
        /// remembered default target/format is used silently.</summary>
        public bool AskBeforeEachJob { get; set; } = true;

        /// <summary>Name of the format pre-selected in the picker / used as default.</summary>
        public string DefaultFormatName { get; set; }

        /// <summary>User-defined formats, shown alongside the built-ins.</summary>
        public List<PrintFormat> CustomFormats { get; set; } = new List<PrintFormat>();

        /// <summary>Built-in formats plus any the user added, de-duplicated by name.</summary>
        public List<PrintFormat> AllFormats()
        {
            var all = PrintFormat.BuiltIns();
            foreach (var f in CustomFormats)
            {
                if (string.IsNullOrWhiteSpace(f?.Name)) continue;
                all.RemoveAll(x => string.Equals(x.Name, f.Name, System.StringComparison.OrdinalIgnoreCase));
                all.Add(f);
            }
            return all;
        }

        public void Save(string path) => File.WriteAllText(path, Json.Serialize(this));

        public static AppConfig Load(string path)
        {
            if (!File.Exists(path)) return new AppConfig();
            var cfg = Json.Deserialize<AppConfig>(File.ReadAllText(path));
            return cfg ?? new AppConfig();
        }

        public static string DefaultPath =>
            Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "PrintBridge", "config.json");
    }
}
