using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace PicMark
{
    internal sealed class ImageConversionOptions
    {
        public string TargetExtension { get; set; } = ".png";
        public int Quality { get; set; } = 90;
        public bool OverwriteExisting { get; set; }
    }

    internal sealed class ImageConversionResult
    {
        public string SourcePath { get; set; }
        public string TargetPath { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public long SourceBytes { get; set; }
        public long TargetBytes { get; set; }
    }

    internal static class ImageConversionService
    {
        public static readonly string[] InputExtensions =
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif", ".tif", ".tiff", ".heic", ".heif"
        };

        public static readonly string[] OutputExtensions = { ".png", ".jpg", ".webp", ".bmp" };

        public static string BuildOpenFilter()
        {
            return "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif;*.tif;*.tiff;*.heic;*.heif|常见图片|*.jpg;*.jpeg;*.png;*.bmp;*.webp|所有文件|*.*";
        }

        public static bool IsSupportedInput(string path)
        {
            string ext = Path.GetExtension(path)?.ToLowerInvariant();
            return InputExtensions.Contains(ext);
        }

        public static bool IsSupportedOutput(string extension)
        {
            extension = NormalizeExtension(extension);
            return OutputExtensions.Contains(extension);
        }

        public static string NormalizeExtension(string extension)
        {
            extension = (extension ?? ".png").Trim().ToLowerInvariant();
            if (!extension.StartsWith(".")) extension = "." + extension;
            if (extension == ".jpeg") return ".jpg";
            return extension;
        }

        public static ImageConversionResult Convert(string sourcePath, string outputDirectory, ImageConversionOptions options)
        {
            var result = new ImageConversionResult { SourcePath = sourcePath };
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                    throw new FileNotFoundException("源文件不存在。", sourcePath);

                if (!IsSupportedInput(sourcePath))
                    throw new NotSupportedException("暂不支持这个输入格式。");

                Directory.CreateDirectory(outputDirectory);
                string targetExt = NormalizeExtension(options.TargetExtension);
                if (!IsSupportedOutput(targetExt))
                    throw new NotSupportedException("暂不支持这个输出格式。");

                BitmapSource bitmap = LoadBitmap(sourcePath);
                string targetPath = BuildTargetPath(sourcePath, outputDirectory, targetExt, options.OverwriteExisting);
                byte[] bytes = EncodeBitmap(bitmap, targetExt, options.Quality);
                File.WriteAllBytes(targetPath, bytes);

                result.Success = true;
                result.TargetPath = targetPath;
                result.SourceBytes = new FileInfo(sourcePath).Length;
                result.TargetBytes = bytes.Length;
                result.Message = "完成";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
            }

            return result;
        }

        public static BitmapSource LoadBitmap(string path)
        {
            string ext = Path.GetExtension(path)?.ToLowerInvariant();
            if (ext == ".webp")
                return WebpDecoder.Load(path);
            if (ext == ".heic" || ext == ".heif")
                return LoadWithWic(path, "当前系统没有可用的 HEIC/HEIF 解码器。Win7 通常需要先安装系统级 HEIF 解码支持。");
            return LoadWithWic(path, "无法解析这张图片，文件可能已损坏或系统缺少对应解码器。");
        }

        public static byte[] EncodeBitmap(BitmapSource source, string extension, int quality)
        {
            extension = NormalizeExtension(extension);
            if (extension == ".webp")
                return EncodeWebp(source, quality);
            return MainWindow.EncodeBitmap(PrepareForExtension(source, extension), extension, quality);
        }

        private static BitmapSource LoadWithWic(string path, string failureMessage)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"{failureMessage}\n{ex.Message}");
            }
        }

        private static BitmapSource PrepareForExtension(BitmapSource source, string extension)
        {
            if (extension == ".jpg")
                return FlattenTransparency(source);
            return source;
        }

        private static BitmapSource FlattenTransparency(BitmapSource source)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
                dc.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            }
            var bitmap = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private static byte[] EncodeWebp(BitmapSource source, int quality)
        {
            byte[] pngBytes = MainWindow.EncodeBitmap(source, ".png", 100);
            using (var bitmap = SKBitmap.Decode(pngBytes))
            {
                if (bitmap == null)
                    throw new InvalidOperationException("无法准备 WebP 编码数据。");
                using (var image = SKImage.FromBitmap(bitmap))
                using (var data = image.Encode(SKEncodedImageFormat.Webp, Math.Max(1, Math.Min(100, quality))))
                {
                    if (data == null)
                        throw new InvalidOperationException("WebP 编码失败。");
                    return data.ToArray();
                }
            }
        }

        private static string BuildTargetPath(string sourcePath, string outputDirectory, string targetExt, bool overwrite)
        {
            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            string path = Path.Combine(outputDirectory, baseName + targetExt);
            if (overwrite || !File.Exists(path)) return path;

            int index = 2;
            while (true)
            {
                string candidate = Path.Combine(outputDirectory, $"{baseName}({index}){targetExt}");
                if (!File.Exists(candidate)) return candidate;
                index++;
            }
        }
    }
}
