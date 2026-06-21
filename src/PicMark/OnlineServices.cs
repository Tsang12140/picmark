using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;

namespace PicMark
{
    internal static class OnlineServices
    {
        public const string UpdateManifestUrl = "https://raw.githubusercontent.com/Tsang12140/picmark/main/docs/update.json";

        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public static async Task<UpdateCheckResult> CheckForUpdateAsync(string currentVersion)
        {
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) })
                {
                    string json = await client.GetStringAsync(UpdateManifestUrl).ConfigureAwait(false);
                    var manifest = Json.Deserialize<UpdateManifest>(json);
                    if (manifest == null || string.IsNullOrWhiteSpace(manifest.latestVersion))
                        return UpdateCheckResult.Failed();

                    bool hasUpdate = CompareVersion(manifest.latestVersion, currentVersion) > 0;
                    return new UpdateCheckResult
                    {
                        Success = true,
                        HasUpdate = hasUpdate,
                        LatestVersion = manifest.latestVersion,
                        ReleaseUrl = manifest.releaseUrl,
                        SetupUrl = manifest.setupUrl,
                        PortableUrl = manifest.portableUrl,
                        TelemetryUrl = manifest.telemetryUrl,
                        Notes = manifest.notes ?? new List<string>()
                    };
                }
            }
            catch
            {
                return UpdateCheckResult.Failed();
            }
        }

        public static async Task<bool> SendDailyTelemetryAsync(AppSettings settings, string appVersion, string telemetryUrl)
        {
            if (settings == null) return false;
            if (!string.Equals(settings.TelemetryConsent, "Allowed", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.IsNullOrWhiteSpace(telemetryUrl)) return false;

            string today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (string.Equals(settings.LastTelemetryDateUtc, today, StringComparison.OrdinalIgnoreCase)) return false;

            try
            {
                var payload = new Dictionary<string, object>
                {
                    { "event", "app_start" },
                    { "appVersion", appVersion },
                    { "channel", DetectChannel() },
                    { "os", GetOsName() },
                    { "screenBucket", GetScreenBucket() },
                    { "isLowResolution", IsLowResolution() },
                    { "installId", EnsureInstallId(settings) },
                    { "dateUtc", today }
                };

                string json = Json.Serialize(payload);
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) })
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    await client.PostAsync(telemetryUrl, content).ConfigureAwait(false);
                }

                settings.LastTelemetryDateUtc = today;
                settings.Save();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool ShouldCheckUpdate(AppSettings settings)
        {
            if (settings == null || !settings.AutoCheckUpdates) return false;
            if (!TryParseUtc(settings.LastUpdateCheckUtc, out DateTime last)) return true;
            return DateTime.UtcNow - last >= TimeSpan.FromDays(3);
        }

        public static void MarkUpdateChecked(AppSettings settings)
        {
            if (settings == null) return;
            settings.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            settings.Save();
        }

        private static int CompareVersion(string left, string right)
        {
            if (!Version.TryParse(NormalizeVersion(left), out Version a)) return 0;
            if (!Version.TryParse(NormalizeVersion(right), out Version b)) return 0;
            return a.CompareTo(b);
        }

        private static string NormalizeVersion(string value)
        {
            value = (value ?? string.Empty).Trim().TrimStart('v', 'V');
            return string.IsNullOrWhiteSpace(value) ? "0.0.0" : value;
        }

        private static bool TryParseUtc(string value, out DateTime result)
        {
            return DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out result);
        }

        private static string EnsureInstallId(AppSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.InstallId)) return settings.InstallId;
            settings.InstallId = Guid.NewGuid().ToString("N");
            settings.Save();
            return settings.InstallId;
        }

        private static string DetectChannel()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
            return baseDir.IndexOf("Program Files", StringComparison.OrdinalIgnoreCase) >= 0 ? "setup" : "portable";
        }

        private static string GetOsName()
        {
            Version version = Environment.OSVersion.Version;
            if (version.Major == 6 && version.Minor == 1) return "Windows 7";
            if (version.Major == 6 && version.Minor == 2) return "Windows 8";
            if (version.Major == 6 && version.Minor == 3) return "Windows 8.1";
            if (version.Major >= 10) return "Windows 10+";
            return "Windows";
        }

        private static string GetScreenBucket()
        {
            int width = RoundToBucket(SystemParameters.PrimaryScreenWidth);
            int height = RoundToBucket(SystemParameters.PrimaryScreenHeight);
            return width + "x" + height;
        }

        private static bool IsLowResolution()
        {
            return SystemParameters.PrimaryScreenWidth <= 1366 || SystemParameters.PrimaryScreenHeight <= 768;
        }

        private static int RoundToBucket(double value)
        {
            int number = Math.Max(0, (int)Math.Round(value));
            if (number <= 0) return 0;
            return (int)Math.Round(number / 100.0) * 100;
        }
    }

    internal sealed class UpdateCheckResult
    {
        public bool Success { get; set; }
        public bool HasUpdate { get; set; }
        public string LatestVersion { get; set; }
        public string ReleaseUrl { get; set; }
        public string SetupUrl { get; set; }
        public string PortableUrl { get; set; }
        public string TelemetryUrl { get; set; }
        public List<string> Notes { get; set; } = new List<string>();

        public static UpdateCheckResult Failed()
        {
            return new UpdateCheckResult { Success = false };
        }
    }

    internal sealed class UpdateManifest
    {
        public string latestVersion { get; set; }
        public string releaseUrl { get; set; }
        public string setupUrl { get; set; }
        public string portableUrl { get; set; }
        public string telemetryUrl { get; set; }
        public List<string> notes { get; set; }
    }
}
