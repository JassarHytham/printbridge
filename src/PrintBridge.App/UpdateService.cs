using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PrintBridge.App
{
    /// <summary>Details of the newest published GitHub release.</summary>
    public class UpdateInfo
    {
        public Version Version { get; set; }
        public string TagName { get; set; }
        public string ReleaseName { get; set; }
        public string Notes { get; set; }
        public string DownloadUrl { get; set; }   // installer .exe asset, may be null
    }

    /// <summary>
    /// Checks GitHub Releases for a newer build and downloads the installer asset.
    /// Uses the unauthenticated GitHub API (60 requests/hour/IP — ample for manual
    /// "Check for updates" clicks).
    /// </summary>
    public class UpdateService
    {
        private const string Owner = "JassarHytham";
        private const string Repo  = "printbridge";
        private const string LatestReleaseUrl =
            "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";

        static UpdateService()
        {
            // GitHub requires TLS 1.2+. .NET 4.8 usually negotiates it, but be explicit.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        /// <summary>This build's version (from the assembly / csproj &lt;Version&gt;).</summary>
        public Version CurrentVersion => Normalize(Assembly.GetExecutingAssembly().GetName().Version);

        /// <summary>Latest release, or null if the repo has no published releases.</summary>
        public async Task<UpdateInfo> GetLatestAsync()
        {
            string json;
            try
            {
                using (var wc = NewClient())
                    json = await wc.DownloadStringTaskAsync(LatestReleaseUrl).ConfigureAwait(false);
            }
            catch (WebException ex) when ((ex.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound)
            {
                // GitHub returns 404 for /releases/latest when the repo has no published
                // (non-draft, non-prerelease) releases yet. Not an error — just nothing to offer.
                return null;
            }

            var o = JObject.Parse(json);
            var tag = (string)o["tag_name"];
            if (string.IsNullOrEmpty(tag)) return null;

            var asset = o["assets"]?.FirstOrDefault(a =>
                ((string)a["name"])?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true);

            return new UpdateInfo
            {
                TagName     = tag,
                Version     = ParseVersion(tag),
                ReleaseName = (string)o["name"],
                Notes       = (string)o["body"],
                DownloadUrl = (string)asset?["browser_download_url"]
            };
        }

        public bool IsNewer(UpdateInfo info) =>
            info?.Version != null && Normalize(info.Version) > CurrentVersion;

        /// <summary>Downloads the installer to a temp file and returns its full path.</summary>
        public async Task<string> DownloadInstallerAsync(UpdateInfo info)
        {
            if (string.IsNullOrEmpty(info?.DownloadUrl))
                throw new InvalidOperationException("This release has no installer (.exe) attached.");

            byte[] bytes;
            using (var wc = NewClient())
                bytes = await wc.DownloadDataTaskAsync(info.DownloadUrl).ConfigureAwait(false);

            var path = Path.Combine(Path.GetTempPath(), $"PrintBridge-Setup-{Sanitize(info.TagName)}.exe");
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static WebClient NewClient()
        {
            var wc = new WebClient();
            wc.Headers.Add("User-Agent", "PrintBridge-Updater");   // GitHub rejects requests without one
            wc.Headers.Add("Accept", "application/vnd.github+json");
            return wc;
        }

        private static Version ParseVersion(string tag)
        {
            // Tolerate "v1.2.3", "1.2.3", "1.2".
            var t = (tag ?? "").TrimStart('v', 'V').Trim();
            return Version.TryParse(t, out var v) ? v : null;
        }

        // Compare on major.minor.build only (ignore the 4th component / unspecified parts).
        private static Version Normalize(Version v) =>
            new Version(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build));

        private static string Sanitize(string s) =>
            string.Concat((s ?? "latest").Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_'));
    }
}
