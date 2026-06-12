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
