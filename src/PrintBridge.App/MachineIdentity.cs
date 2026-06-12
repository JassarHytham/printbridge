using System;
using System.IO;

namespace PrintBridge.App
{
    /// <summary>Stable per-PC id persisted next to the config.</summary>
    public static class MachineIdentity
    {
        public static string GetOrCreatePcId()
        {
            var path = Path.Combine(Path.GetDirectoryName(AppConfig.DefaultPath), "pcid.txt");
            if (File.Exists(path)) return File.ReadAllText(path).Trim();
            var id = Guid.NewGuid().ToString();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, id);
            return id;
        }
    }
}
