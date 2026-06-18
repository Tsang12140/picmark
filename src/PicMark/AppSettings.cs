using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
                "HistoryCacheMb=" + HistoryCacheMb
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
    }
}
