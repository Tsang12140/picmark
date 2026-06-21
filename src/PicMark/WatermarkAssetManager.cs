using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PicMark
{
    internal static class WatermarkAssetManager
    {
        private static readonly string AssetDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PicMark",
            "WatermarkAssets");

        private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };

        public static string Filter =>
            "Logo images|*.png;*.jpg;*.jpeg;*.bmp;*.webp|PNG|*.png|JPG|*.jpg;*.jpeg|WebP|*.webp|BMP|*.bmp";

        public static bool IsSupported(string path)
        {
            string ext = Path.GetExtension(path);
            return SupportedExtensions.Any(x => string.Equals(x, ext, StringComparison.OrdinalIgnoreCase));
        }

        public static string ImportAsset(string sourcePath, AppSettings settings, out string removedPath)
        {
            removedPath = null;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("Logo file not found.", sourcePath);
            if (!IsSupported(sourcePath))
                throw new InvalidOperationException("Unsupported logo format.");

            Directory.CreateDirectory(AssetDirectory);
            PruneMissing(settings);

            string sourceFullPath = Path.GetFullPath(sourcePath);
            string existing = settings.WatermarkLogoAssets
                .FirstOrDefault(path => string.Equals(SafeFullPath(path), sourceFullPath, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(existing))
            {
                Touch(existing, settings);
                return existing;
            }

            int limit = Math.Max(1, settings.WatermarkAssetLimit);
            if (settings.WatermarkLogoAssets.Count >= limit)
            {
                removedPath = settings.WatermarkLogoAssets[settings.WatermarkLogoAssets.Count - 1];
                settings.WatermarkLogoAssets.RemoveAt(settings.WatermarkLogoAssets.Count - 1);
                TryDeleteOwnedAsset(removedPath);
            }

            string copiedPath = CopyOriginal(sourcePath);
            settings.WatermarkLogoAssets.Insert(0, copiedPath);
            return copiedPath;
        }

        public static void Touch(string assetPath, AppSettings settings)
        {
            if (string.IsNullOrWhiteSpace(assetPath)) return;
            PruneMissing(settings);
            settings.WatermarkLogoAssets.RemoveAll(path => string.Equals(path, assetPath, StringComparison.OrdinalIgnoreCase));
            settings.WatermarkLogoAssets.Insert(0, assetPath);
            EnforceLimit(settings);
        }

        public static void EnforceLimit(AppSettings settings)
        {
            int limit = Math.Max(1, settings.WatermarkAssetLimit);
            while (settings.WatermarkLogoAssets.Count > limit)
            {
                string removed = settings.WatermarkLogoAssets[settings.WatermarkLogoAssets.Count - 1];
                settings.WatermarkLogoAssets.RemoveAt(settings.WatermarkLogoAssets.Count - 1);
                TryDeleteOwnedAsset(removed);
            }
        }

        public static void PruneMissing(AppSettings settings)
        {
            settings.WatermarkLogoAssets.RemoveAll(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path));
        }

        private static string CopyOriginal(string sourcePath)
        {
            string ext = Path.GetExtension(sourcePath);
            string name = Path.GetFileNameWithoutExtension(sourcePath);
            string safeName = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            if (safeName.Length > 48) safeName = safeName.Substring(0, 48);
            string candidate = Path.Combine(AssetDirectory, $"{DateTime.Now:yyyyMMdd_HHmmss}_{safeName}{ext}");
            int index = 2;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(AssetDirectory, $"{DateTime.Now:yyyyMMdd_HHmmss}_{safeName}_{index}{ext}");
                index++;
            }
            File.Copy(sourcePath, candidate, false);
            return candidate;
        }

        private static void TryDeleteOwnedAsset(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
                string dir = Path.GetFullPath(Path.GetDirectoryName(path) ?? string.Empty);
                string ownedDir = Path.GetFullPath(AssetDirectory);
                if (string.Equals(dir, ownedDir, StringComparison.OrdinalIgnoreCase))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static string SafeFullPath(string path)
        {
            try { return Path.GetFullPath(path); }
            catch { return path ?? string.Empty; }
        }
    }
}
