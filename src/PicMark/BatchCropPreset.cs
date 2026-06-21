using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PicMark
{
    public class BatchCropPreset
    {
        public string Name;
        public double Top;
        public double Bottom;
        public double Left;
        public double Right;
    }

    public static class BatchCropPresetStore
    {
        private static readonly string StorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PicMark",
            "batch_crop_presets.txt");

        public static List<BatchCropPreset> Load()
        {
            var presets = new List<BatchCropPreset>();
            try
            {
                if (!File.Exists(StorePath)) return presets;
                foreach (var rawLine in File.ReadAllLines(StorePath))
                {
                    var parts = rawLine.Split('|');
                    if (parts.Length >= 6 && parts[0] == "PRESET")
                    {
                        presets.Add(new BatchCropPreset
                        {
                            Name = parts[1],
                            Top = ParseDouble(parts[2]),
                            Bottom = ParseDouble(parts[3]),
                            Left = ParseDouble(parts[4]),
                            Right = ParseDouble(parts[5])
                        });
                    }
                }
            }
            catch
            {
                // ????????????????????????
            }
            return presets;
        }

        public static void Save(List<BatchCropPreset> presets)
        {
            try
            {
                string dir = Path.GetDirectoryName(StorePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                var lines = new List<string>();
                foreach (var preset in presets)
                {
                    lines.Add(string.Join("|", "PRESET", preset.Name,
                        preset.Top.ToString(CultureInfo.InvariantCulture),
                        preset.Bottom.ToString(CultureInfo.InvariantCulture),
                        preset.Left.ToString(CultureInfo.InvariantCulture),
                        preset.Right.ToString(CultureInfo.InvariantCulture)));
                }
                File.WriteAllLines(StorePath, lines);
            }
            catch
            {
                // ?????????????????
            }
        }

        private static double ParseDouble(string value) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 0;
    }
}
