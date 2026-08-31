using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace PicMark
{
    public sealed class WatermarkPreset
    {
        public string Text { get; set; } = string.Empty;
        public string Template { get; set; } = "CertificateGrid";
        public string FontFamily { get; set; } = "Microsoft YaHei UI";
        public bool Bold { get; set; }
        public string Color { get; set; } = "#FF000000";
        public double Opacity { get; set; } = 20;
        public double FontSize { get; set; } = 28;
        public double Angle { get; set; }
        public double Spacing { get; set; } = 204;
        public double HorizontalOffset { get; set; }
        public double VerticalOffset { get; set; }
        public string LogoPath { get; set; } = string.Empty;
        public double LogoScalePercent { get; set; } = 18;
        public bool LogoFlipHorizontal { get; set; }
        public bool LogoFlipVertical { get; set; }

        public WatermarkPreset Clone()
        {
            return (WatermarkPreset)MemberwiseClone();
        }

        public bool IsEquivalentTo(WatermarkPreset other)
        {
            if (other == null) return false;
            return string.Equals(Text, other.Text, StringComparison.Ordinal) &&
                   string.Equals(Template, other.Template, StringComparison.Ordinal) &&
                   string.Equals(FontFamily, other.FontFamily, StringComparison.Ordinal) &&
                   Bold == other.Bold &&
                   string.Equals(Color, other.Color, StringComparison.OrdinalIgnoreCase) &&
                   NearlyEquals(Opacity, other.Opacity) &&
                   NearlyEquals(FontSize, other.FontSize) &&
                   NearlyEquals(Angle, other.Angle) &&
                   NearlyEquals(Spacing, other.Spacing) &&
                   NearlyEquals(HorizontalOffset, other.HorizontalOffset) &&
                   NearlyEquals(VerticalOffset, other.VerticalOffset) &&
                   string.Equals(LogoPath, other.LogoPath, StringComparison.OrdinalIgnoreCase) &&
                   NearlyEquals(LogoScalePercent, other.LogoScalePercent) &&
                   LogoFlipHorizontal == other.LogoFlipHorizontal &&
                   LogoFlipVertical == other.LogoFlipVertical;
        }

        private static bool NearlyEquals(double left, double right) => Math.Abs(left - right) < 0.01;
    }

    public class AppSettings
    {
        private const string InstallerOptionsRegistryKey = @"Software\PicMark\InstallOptions";
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PicMark",
            "settings.txt");

        public double WindowLeft { get; set; } = double.NaN;
        public double WindowTop { get; set; } = double.NaN;
        public double WindowWidth { get; set; } = 1080;
        public double WindowHeight { get; set; } = 720;
        public WindowState WindowState { get; set; } = WindowState.Normal;
        public bool WindowLayoutInitialized { get; set; }
        public string Tool { get; set; } = "Select";
        public string Color { get; set; } = "Red";
        public string Thickness { get; set; } = "9";
        public string FontSize { get; set; } = "36";
        public int HistoryCacheMb { get; set; } = 500;
        public int WatermarkAssetLimit { get; set; } = 12;
        public string WatermarkTemplate { get; set; } = "CertificateGrid";
        public string WatermarkText { get; set; } = string.Empty;
        public WatermarkPreset LastWatermarkPreset { get; set; } = new WatermarkPreset();
        public string RecentContextAction { get; set; } = string.Empty;
        public bool AutoCheckUpdates { get; set; } = true;
        public string TelemetryConsent { get; set; } = "Denied";
        public string InstallId { get; set; } = Guid.NewGuid().ToString("N");
        public string LastUpdateCheckUtc { get; set; } = string.Empty;
        public string IgnoredUpdateVersion { get; set; } = string.Empty;
        public string DomesticUpdateFallbackDate { get; set; } = string.Empty;
        public int DomesticUpdateFallbackCount { get; set; }
        public string LastTelemetryDateUtc { get; set; } = string.Empty;
        public string LastTelemetryUrl { get; set; } = string.Empty;
        public List<string> RecentFiles { get; } = new List<string>();
        public List<string> WatermarkLogoAssets { get; } = new List<string>();
        public List<string> WatermarkTextHistory { get; } = new List<string>();
        public List<WatermarkPreset> WatermarkPresetHistory { get; } = new List<WatermarkPreset>();
        private bool _needsSave;

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            bool settingsFileExists = false;
            try
            {
                settingsFileExists = File.Exists(SettingsPath);
                if (settingsFileExists)
                {
                    foreach (var line in File.ReadAllLines(SettingsPath))
                    {
                        int split = line.IndexOf('=');
                        if (split <= 0) continue;
                        string key = line.Substring(0, split);
                        string value = line.Substring(split + 1);
                        settings.SetValue(key, value);
                    }
                }
            }
            catch
            {
                // 配置加载失败时返回默认设置，不阻塞启动
            }

            bool installerOptionsApplied = ApplyPendingInstallerOptions(settings, settingsFileExists);
            settings.MigrateLegacyWatermarkPresets();
            if (installerOptionsApplied || settings._needsSave)
                settings.Save();
            return settings;
        }

        private static bool ApplyPendingInstallerOptions(AppSettings settings, bool settingsFileExists)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(InstallerOptionsRegistryKey, true))
                {
                    if (key == null) return false;

                    bool changed = false;
                    if (!settingsFileExists)
                    {
                        string autoCheckUpdates = Convert.ToString(key.GetValue("AutoCheckUpdates"));
                        string telemetryConsent = Convert.ToString(key.GetValue("TelemetryConsent"));

                        if (!string.IsNullOrWhiteSpace(autoCheckUpdates))
                        {
                            settings.AutoCheckUpdates = ParseBool(autoCheckUpdates, settings.AutoCheckUpdates);
                            changed = true;
                        }

                        if (!string.IsNullOrWhiteSpace(telemetryConsent))
                        {
                            settings.TelemetryConsent = NormalizeTelemetryConsent(telemetryConsent);
                            changed = true;
                        }
                    }

                    key.DeleteValue("AutoCheckUpdates", false);
                    key.DeleteValue("TelemetryConsent", false);
                    return changed;
                }
            }
            catch
            {
                // The first-run installer choices are optional; safe local defaults remain in effect.
                return false;
            }
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
                "WindowLayoutInitialized=" + WindowLayoutInitialized,
                "Tool=" + Tool,
                "Color=" + Color,
                "Thickness=" + Thickness,
                "FontSize=" + FontSize,
                "HistoryCacheMb=" + HistoryCacheMb,
                "WatermarkAssetLimit=" + WatermarkAssetLimit,
                "WatermarkTemplate=" + WatermarkTemplate,
                "WatermarkText=" + Uri.EscapeDataString(WatermarkText ?? string.Empty),
                "LastWatermarkPreset=" + SerializePreset(LastWatermarkPreset),
                "RecentContextAction=" + RecentContextAction,
                "AutoCheckUpdates=" + AutoCheckUpdates,
                "TelemetryConsent=" + TelemetryConsent,
                "InstallId=" + InstallId,
                "LastUpdateCheckUtc=" + LastUpdateCheckUtc,
                "IgnoredUpdateVersion=" + IgnoredUpdateVersion,
                "DomesticUpdateFallbackDate=" + DomesticUpdateFallbackDate,
                "DomesticUpdateFallbackCount=" + DomesticUpdateFallbackCount,
                "LastTelemetryDateUtc=" + LastTelemetryDateUtc,
                "LastTelemetryUrl=" + LastTelemetryUrl,
                "RecentFiles=" + string.Join("|", RecentFiles.Select(Uri.EscapeDataString)),
                "WatermarkLogoAssets=" + string.Join("|", WatermarkLogoAssets.Select(Uri.EscapeDataString)),
                "WatermarkPresetHistory=" + string.Join("|", WatermarkPresetHistory.Select(SerializePreset))
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
                case "WindowLayoutInitialized": WindowLayoutInitialized = ParseBool(value, WindowLayoutInitialized); break;
                case "Tool": Tool = value; break;
                case "Color": Color = value; break;
                case "Thickness": Thickness = value; break;
                case "FontSize": FontSize = value; break;
                case "HistoryCacheMb": HistoryCacheMb = ParseInt(value, HistoryCacheMb); break;
                case "WatermarkAssetLimit": WatermarkAssetLimit = Math.Max(1, ParseInt(value, WatermarkAssetLimit)); break;
                case "WatermarkTemplate": WatermarkTemplate = value; break;
                case "WatermarkText": WatermarkText = Uri.UnescapeDataString(value ?? string.Empty); break;
                case "LastWatermarkPreset":
                    var lastPreset = DeserializePreset(value);
                    if (lastPreset != null) LastWatermarkPreset = lastPreset;
                    break;
                case "RecentContextAction": RecentContextAction = value ?? string.Empty; break;
                case "AutoCheckUpdates": AutoCheckUpdates = ParseBool(value, AutoCheckUpdates); break;
                case "TelemetryConsent":
                    TelemetryConsent = NormalizeTelemetryConsent(value);
                    if (!string.Equals(TelemetryConsent, value, StringComparison.OrdinalIgnoreCase)) _needsSave = true;
                    break;
                case "InstallId": InstallId = string.IsNullOrWhiteSpace(value) ? InstallId : value; break;
                case "LastUpdateCheckUtc": LastUpdateCheckUtc = value ?? string.Empty; break;
                case "IgnoredUpdateVersion": IgnoredUpdateVersion = value ?? string.Empty; break;
                case "DomesticUpdateFallbackDate": DomesticUpdateFallbackDate = value ?? string.Empty; break;
                case "DomesticUpdateFallbackCount": DomesticUpdateFallbackCount = Math.Max(0, ParseInt(value, DomesticUpdateFallbackCount)); break;
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
                case "WatermarkTextHistory":
                    WatermarkTextHistory.Clear();
                    foreach (string item in value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string text = Uri.UnescapeDataString(item);
                        if (!string.IsNullOrWhiteSpace(text)) WatermarkTextHistory.Add(text);
                    }
                    break;
                case "WatermarkPresetHistory":
                    WatermarkPresetHistory.Clear();
                    foreach (string item in value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var preset = DeserializePreset(item);
                        if (preset != null) WatermarkPresetHistory.Add(preset);
                    }
                    break;
            }
        }

        private void MigrateLegacyWatermarkPresets()
        {
            if (LastWatermarkPreset == null) LastWatermarkPreset = new WatermarkPreset();
            if (string.IsNullOrWhiteSpace(LastWatermarkPreset.Text) && !string.IsNullOrWhiteSpace(WatermarkText))
            {
                LastWatermarkPreset.Text = WatermarkText;
                LastWatermarkPreset.Template = string.IsNullOrWhiteSpace(WatermarkTemplate) ? "CertificateGrid" : WatermarkTemplate;
            }

        }

        private static string SerializePreset(WatermarkPreset preset)
        {
            preset = preset ?? new WatermarkPreset();
            return string.Join(";", new[]
            {
                EncodePresetText(preset.Text),
                EncodePresetText(preset.Template),
                EncodePresetText(preset.FontFamily),
                preset.Bold.ToString(),
                EncodePresetText(preset.Color),
                FormatDouble(preset.Opacity),
                FormatDouble(preset.FontSize),
                FormatDouble(preset.Angle),
                FormatDouble(preset.Spacing),
                FormatDouble(preset.HorizontalOffset),
                FormatDouble(preset.VerticalOffset),
                EncodePresetText(preset.LogoPath),
                FormatDouble(preset.LogoScalePercent),
                preset.LogoFlipHorizontal.ToString(),
                preset.LogoFlipVertical.ToString()
            });
        }

        private static WatermarkPreset DeserializePreset(string value)
        {
            try
            {
                string[] fields = (value ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.None);
                if (fields.Length != 15) return null;
                return new WatermarkPreset
                {
                    Text = DecodePresetText(fields[0]),
                    Template = DecodePresetText(fields[1]),
                    FontFamily = DecodePresetText(fields[2]),
                    Bold = ParseBool(fields[3], false),
                    Color = DecodePresetText(fields[4]),
                    Opacity = ParseDouble(fields[5], 20),
                    FontSize = ParseDouble(fields[6], 28),
                    Angle = ParseDouble(fields[7], 0),
                    Spacing = ParseDouble(fields[8], 204),
                    HorizontalOffset = ParseDouble(fields[9], 0),
                    VerticalOffset = ParseDouble(fields[10], 0),
                    LogoPath = DecodePresetText(fields[11]),
                    LogoScalePercent = ParseDouble(fields[12], 18),
                    LogoFlipHorizontal = ParseBool(fields[13], false),
                    LogoFlipVertical = ParseBool(fields[14], false)
                };
            }
            catch
            {
                return null;
            }
        }

        private static string EncodePresetText(string value) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

        private static string DecodePresetText(string value) =>
            Encoding.UTF8.GetString(Convert.FromBase64String(value ?? string.Empty));

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
            return "Denied";
        }
    }
}
