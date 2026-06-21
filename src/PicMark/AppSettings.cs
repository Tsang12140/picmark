using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;

namespace PicMark
{
    public class AppSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PicMark",
            "settings.txt");

        public double WindowLeft { get; set; } = double.NaN;
        public double WindowTop { get; set; } = double.NaN;
        public double WindowWidth { get; set; } = 1280;
        public double WindowHeight { get; set; } = 840;
        public WindowState WindowState { get; set; } = WindowState.Normal;
        public string Tool { get; set; } = "Select";
        public string Color { get; set; } = "Red";
        public string Thickness { get; set; } = "9";
        public string FontSize { get; set; } = "36";
        public int HistoryCacheMb { get; set; } = 500;
        public int WatermarkAssetLimit { get; set; } = 12;
        public string WatermarkTemplate { get; set; } = "CertificateGrid";
        public string RecentContextAction { get; set; } = string.Empty;
        public bool AutoCheckUpdates { get; set; } = true;
        public string TelemetryConsent { get; set; } = "Ask";
        public string InstallId { get; set; } = Guid.NewGuid().ToString("N");
        public string LastUpdateCheckUtc { get; set; } = string.Empty;
        public string IgnoredUpdateVersion { get; set; } = string.Empty;
        public string LastTelemetryDateUtc { get; set; } = string.Empty;
        public string LastTelemetryUrl { get; set; } = string.Empty;
        public List<string> RecentFiles { get; } = new List<string>();
        public List<string> WatermarkLogoAssets { get; } = new List<string>();

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            try
            {
                if (!File.Exists(SettingsPath)) return settings;

                foreach (var line in File.ReadAllLines(SettingsPath))
                {
                    int split = line.IndexOf('=');
                    if (split <= 0) continue;
                    string key = line.Substring(0, split);
                    string value = line.Substring(split + 1);
                    settings.SetValue(key, value);
                }
            }
            catch
            {
                // 配置加载失败时返回默认设置，不阻塞启动
            }
            return settings;
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                var lines = new[]
            {
                "WindowLeft=" + FormatDouble(WindowLeft),
                "WindowTop=" + FormatDouble(WindowTop),
                "WindowWidth=" + FormatDouble(WindowWidth),
                "WindowHeight=" + FormatDouble(WindowHeight),
                "WindowState=" + WindowState,
                "Tool=" + Tool,
                "Color=" + Color,
                "Thickness=" + Thickness,
                "FontSize=" + FontSize,
                "HistoryCacheMb=" + HistoryCacheMb,
                "WatermarkAssetLimit=" + WatermarkAssetLimit,
                "WatermarkTemplate=" + WatermarkTemplate,
                "RecentContextAction=" + RecentContextAction,
                "AutoCheckUpdates=" + AutoCheckUpdates,
                "TelemetryConsent=" + TelemetryConsent,
                "InstallId=" + InstallId,
                "LastUpdateCheckUtc=" + LastUpdateCheckUtc,
                "IgnoredUpdateVersion=" + IgnoredUpdateVersion,
                "LastTelemetryDateUtc=" + LastTelemetryDateUtc,
                "LastTelemetryUrl=" + LastTelemetryUrl,
                "RecentFiles=" + string.Join("|", RecentFiles.Select(Uri.EscapeDataString)),
                "WatermarkLogoAssets=" + string.Join("|", WatermarkLogoAssets.Select(Uri.EscapeDataString))
            };
            File.WriteAllLines(SettingsPath, lines);
            }
            catch
            {
                // 静默失败：配置保存不应影响用户体验
            }
        }

        private void SetValue(string key, string value)
        {
            switch (key)
            {
                case "WindowLeft": WindowLeft = ParseDouble(value, WindowLeft); break;
                case "WindowTop": WindowTop = ParseDouble(value, WindowTop); break;
                case "WindowWidth": WindowWidth = ParseDouble(value, WindowWidth); break;
                case "WindowHeight": WindowHeight = ParseDouble(value, WindowHeight); break;
                case "WindowState":
                    if (Enum.TryParse(value, out WindowState state)) WindowState = state;
                    break;
                case "Tool": Tool = value; break;
                case "Color": Color = value; break;
                case "Thickness": Thickness = value; break;
                case "FontSize": FontSize = value; break;
                case "HistoryCacheMb": HistoryCacheMb = ParseInt(value, HistoryCacheMb); break;
                case "WatermarkAssetLimit": WatermarkAssetLimit = Math.Max(1, ParseInt(value, WatermarkAssetLimit)); break;
                case "WatermarkTemplate": WatermarkTemplate = value; break;
                case "RecentContextAction": RecentContextAction = value ?? string.Empty; break;
                case "AutoCheckUpdates": AutoCheckUpdates = ParseBool(value, AutoCheckUpdates); break;
                case "TelemetryConsent": TelemetryConsent = NormalizeTelemetryConsent(value); break;
                case "InstallId": InstallId = string.IsNullOrWhiteSpace(value) ? InstallId : value; break;
                case "LastUpdateCheckUtc": LastUpdateCheckUtc = value ?? string.Empty; break;
                case "IgnoredUpdateVersion": IgnoredUpdateVersion = value ?? string.Empty; break;
                case "LastTelemetryDateUtc": LastTelemetryDateUtc = value ?? string.Empty; break;
                case "LastTelemetryUrl": LastTelemetryUrl = value ?? string.Empty; break;
                case "RecentFiles":
                    RecentFiles.Clear();
                    foreach (string item in value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string path = Uri.UnescapeDataString(item);
                        if (!string.IsNullOrWhiteSpace(path)) RecentFiles.Add(path);
                    }
                    break;
                case "WatermarkLogoAssets":
                    WatermarkLogoAssets.Clear();
                    foreach (string item in value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string path = Uri.UnescapeDataString(item);
                        if (!string.IsNullOrWhiteSpace(path)) WatermarkLogoAssets.Add(path);
                    }
                    break;
            }
        }

        private static string FormatDouble(double value) =>
            value.ToString(CultureInfo.InvariantCulture);

        private static double ParseDouble(string value, double fallback)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : fallback;
        }

        private static int ParseInt(string value, int fallback)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : fallback;
        }

        private static bool ParseBool(string value, bool fallback)
        {
            return bool.TryParse(value, out bool parsed) ? parsed : fallback;
        }

        private static string NormalizeTelemetryConsent(string value)
        {
            if (string.Equals(value, "Allowed", StringComparison.OrdinalIgnoreCase)) return "Allowed";
            if (string.Equals(value, "Denied", StringComparison.OrdinalIgnoreCase)) return "Denied";
            return "Ask";
        }
    }
}
