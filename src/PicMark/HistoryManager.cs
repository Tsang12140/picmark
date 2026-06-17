using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace PicMark
{
    public static class HistoryManager
    {
        public static readonly string HistoryDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PicMark",
            "History");

        public static string SaveVersion(RenderTargetBitmap bitmap, string sourcePath, int maxCacheMb, string reason)
        {
            if (bitmap == null) return null;

            Directory.CreateDirectory(HistoryDirectory);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string sourceName = string.IsNullOrWhiteSpace(sourcePath)
                ? "clipboard"
                : Path.GetFileNameWithoutExtension(sourcePath);
            string safeName = MakeSafeFileName(sourceName);
            string imagePath = Path.Combine(HistoryDirectory, $"{timestamp}_{safeName}.png");

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var fs = new FileStream(imagePath, FileMode.Create, FileAccess.Write))
                encoder.Save(fs);

            TrimCache(maxCacheMb);
            return imagePath;
        }

        public static long GetCacheBytes()
        {
            if (!Directory.Exists(HistoryDirectory)) return 0;
            DeleteMetadataSidecars();
            return Directory.GetFiles(HistoryDirectory, "*", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .Where(info => info.Exists)
                .Sum(info => info.Length);
        }

        public static void TrimCache(int maxCacheMb)
        {
            if (!Directory.Exists(HistoryDirectory)) return;
            DeleteMetadataSidecars();
            long maxBytes = Math.Max(20, maxCacheMb) * 1024L * 1024L;
            var files = Directory.GetFiles(HistoryDirectory, "*", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .Where(info => info.Exists)
                .OrderBy(info => info.CreationTimeUtc)
                .ToList();

            long total = files.Sum(info => info.Length);
            foreach (var file in files)
            {
                if (total <= maxBytes) break;
                long length = file.Length;
                try
                {
                    file.Delete();
                    total -= length;
                }
                catch
                {
                    total -= length;
                }
            }
        }

        private static void DeleteMetadataSidecars()
        {
            foreach (var path in Directory.GetFiles(HistoryDirectory, "*.txt", SearchOption.TopDirectoryOnly))
            {
                try { File.Delete(path); }
                catch { }
            }
        }

        private static string MakeSafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "image";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
            string safe = new string(chars).Trim();
            return string.IsNullOrWhiteSpace(safe) ? "image" : safe;
        }
    }
}
