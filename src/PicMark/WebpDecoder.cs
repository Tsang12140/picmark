using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace PicMark
{
    public static class WebpDecoder
    {
        public static BitmapSource Load(string path)
        {
            using (var skBitmap = SKBitmap.Decode(path))
            {
                if (skBitmap == null)
                    throw new InvalidOperationException($"无法解析这张 WEBP 图片，文件可能已损坏。\n路径：{path}");

                using (var converted = skBitmap.Copy(SKColorType.Bgra8888))
                {
                    if (converted == null)
                        throw new InvalidOperationException($"无法解析这张 WEBP 图片，文件可能已损坏。\n路径：{path}");

                    int w = converted.Width, h = converted.Height;
                    int stride = converted.RowBytes;
                    var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, converted.Bytes, stride);
                    bmp.Freeze();
                    return bmp;
                }
            }
        }
    }
}
