using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PicMark
{
    internal static class AnnotationConstants
    {
        // 字体族包含 Win7 通用回退：阿里巴巴普惠体 → 微软雅黑 → 黑体
        public const string DefaultFontFamily = "Alibaba PuHuiTi 3.0, Alibaba PuHuiTi, Microsoft YaHei UI, Microsoft YaHei, SimHei, sans-serif";
    }

    public enum MosaicMode
    {
        Pixelate,
        Blur
    }

    public enum ArrowStyle
    {
        Filled,
        Slim,
        Line,
        Double
    }

    public abstract class Annotation
    {
        public Color StrokeColor { get; set; } = Colors.Red;
        public double Thickness { get; set; } = 6;

        public abstract Rect GetBounds();
        public abstract void Draw(DrawingContext dc, bool selected, BitmapSource sourceImage);
        public abstract void Move(Vector delta);
        public abstract Annotation Clone();

        protected static void DrawSelectionAdorner(DrawingContext dc, Rect bounds)
        {
            bounds.Inflate(6, 6);
            var pen = new Pen(Brushes.DodgerBlue, 2) { DashStyle = DashStyles.Dash };
            pen.Freeze();
            dc.DrawRectangle(null, pen, bounds);
        }
    }

    public class RectAnnotation : Annotation
    {
        public Rect Bounds { get; set; }

        public override Rect GetBounds() => Bounds;

        public override void Draw(DrawingContext dc, bool selected, BitmapSource sourceImage)
        {
            var pen = new Pen(new SolidColorBrush(StrokeColor), Thickness);
            pen.Freeze();
            dc.DrawRectangle(null, pen, Bounds);
            if (selected) DrawSelectionAdorner(dc, Bounds);
        }

        public override void Move(Vector delta) => Bounds = new Rect(Bounds.TopLeft + delta, Bounds.Size);

        public override Annotation Clone() => new RectAnnotation { Bounds = Bounds, StrokeColor = StrokeColor, Thickness = Thickness };
    }

    public class EllipseAnnotation : Annotation
    {
        public Rect Bounds { get; set; }

        public override Rect GetBounds() => Bounds;

        public override void Draw(DrawingContext dc, bool selected, BitmapSource sourceImage)
        {
            var pen = new Pen(new SolidColorBrush(StrokeColor), Thickness);
            pen.Freeze();
            var center = new Point(Bounds.X + Bounds.Width / 2, Bounds.Y + Bounds.Height / 2);
            dc.DrawEllipse(null, pen, center, Bounds.Width / 2, Bounds.Height / 2);
            if (selected) DrawSelectionAdorner(dc, Bounds);
        }

        public override void Move(Vector delta) => Bounds = new Rect(Bounds.TopLeft + delta, Bounds.Size);

        public override Annotation Clone() => new EllipseAnnotation { Bounds = Bounds, StrokeColor = StrokeColor, Thickness = Thickness };
    }

    public class ArrowAnnotation : Annotation
    {
        public Point Start { get; set; }
        public Point End { get; set; }
        public ArrowStyle Style { get; set; } = ArrowStyle.Filled;

        public override Rect GetBounds() => new Rect(Start, End);

        public override void Draw(DrawingContext dc, bool selected, BitmapSource sourceImage)
        {
            var brush = new SolidColorBrush(StrokeColor);
            brush.Freeze();
            var dir = End - Start;
            if (dir.Length > 0.001)
            {
                dir.Normalize();
                switch (Style)
                {
                    case ArrowStyle.Slim:
                        DrawSlimArrow(dc, brush, dir);
                        break;
                    case ArrowStyle.Line:
                        DrawLineArrow(dc, brush, dir, false);
                        break;
                    case ArrowStyle.Double:
                        DrawLineArrow(dc, brush, dir, true);
                        break;
                    case ArrowStyle.Filled:
                    default:
                        DrawFilledArrow(dc, brush, dir);
                        break;
                }
            }

            if (selected) DrawSelectionAdorner(dc, GetBounds());
        }

        private void DrawFilledArrow(DrawingContext dc, Brush brush, Vector dir)
        {
            double length = (End - Start).Length;
            double headLength = Math.Min(length * 0.55, Math.Max(24, Thickness * 5.5));
            double headWidth = Math.Max(18, Thickness * 4.0);
            double tailWidth = Math.Max(5, Thickness * 1.05);
            var normal = new Vector(-dir.Y, dir.X);
            var neck = End - dir * headLength;
            var neckLeft = neck + normal * (tailWidth / 2);
            var neckRight = neck - normal * (tailWidth / 2);
            var headLeft = neck + normal * (headWidth / 2);
            var headRight = neck - normal * (headWidth / 2);

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(Start, true, true);
                ctx.LineTo(neckLeft, true, true);
                ctx.LineTo(headLeft, true, true);
                ctx.LineTo(End, true, true);
                ctx.LineTo(headRight, true, true);
                ctx.LineTo(neckRight, true, true);
            }
            geo.Freeze();
            dc.DrawGeometry(brush, null, geo);
        }

        private void DrawSlimArrow(DrawingContext dc, Brush brush, Vector dir)
        {
            var pen = new Pen(brush, Math.Max(2, Thickness * 0.75)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(pen, Start, End);
            DrawArrowHead(dc, brush, dir, Math.Max(15, Thickness * 3.4), Math.Max(10, Thickness * 2.4), true);
        }

        private void DrawLineArrow(DrawingContext dc, Brush brush, Vector dir, bool bothEnds)
        {
            var pen = new Pen(brush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(pen, Start, End);
            DrawArrowHead(dc, brush, dir, Math.Max(14, Thickness * 3.2), Math.Max(8, Thickness * 2.2), false);
            if (bothEnds)
                DrawArrowHeadAt(dc, brush, Start, -dir, Math.Max(14, Thickness * 3.2), Math.Max(8, Thickness * 2.2), false);
        }

        private void DrawArrowHead(DrawingContext dc, Brush brush, Vector dir, double headLength, double headWidth, bool filled)
        {
            DrawArrowHeadAt(dc, brush, End, dir, headLength, headWidth, filled);
        }

        private static void DrawArrowHeadAt(DrawingContext dc, Brush brush, Point tip, Vector dir, double headLength, double headWidth, bool filled)
        {
            var normal = new Vector(-dir.Y, dir.X);
            var baseCenter = tip - dir * headLength;
            var p1 = baseCenter + normal * (headWidth / 2);
            var p2 = baseCenter - normal * (headWidth / 2);
            if (filled)
            {
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(tip, true, true);
                    ctx.LineTo(p1, true, true);
                    ctx.LineTo(p2, true, true);
                }
                geo.Freeze();
                dc.DrawGeometry(brush, null, geo);
            }
            else
            {
                var pen = new Pen(brush, Math.Max(2, headWidth / 4)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(pen, tip, p1);
                dc.DrawLine(pen, tip, p2);
            }
        }

        public override void Move(Vector delta)
        {
            Start += delta;
            End += delta;
        }

        public override Annotation Clone() => new ArrowAnnotation { Start = Start, End = End, StrokeColor = StrokeColor, Thickness = Thickness, Style = Style };
    }

    public class FreehandAnnotation : Annotation
    {
        public List<Point> Points { get; } = new List<Point>();

        public override Rect GetBounds()
        {
            if (Points.Count == 0) return Rect.Empty;
            double minX = Points.Min(p => p.X), minY = Points.Min(p => p.Y);
            double maxX = Points.Max(p => p.X), maxY = Points.Max(p => p.Y);
            return new Rect(new Point(minX, minY), new Point(maxX, maxY));
        }

        public override void Draw(DrawingContext dc, bool selected, BitmapSource sourceImage)
        {
            if (Points.Count < 2) return;
            var brush = new SolidColorBrush(StrokeColor);
            brush.Freeze();
            var pen = new Pen(brush, Thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(Points[0], false, false);
                // 避免 ToList() 分配，直接逐个添加
                for (int i = 1; i < Points.Count; i++)
                    ctx.LineTo(Points[i], true, true);
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
            if (selected) DrawSelectionAdorner(dc, GetBounds());
        }

        public override void Move(Vector delta)
        {
            for (int i = 0; i < Points.Count; i++) Points[i] += delta;
        }

        public override Annotation Clone()
        {
            var clone = new FreehandAnnotation { StrokeColor = StrokeColor, Thickness = Thickness };
            clone.Points.AddRange(Points);
            return clone;
        }
    }

    public class MosaicAnnotation : Annotation
    {
        public Rect Bounds { get; set; }
        public int BlockSize { get; set; } = 18;
        public MosaicMode Mode { get; set; } = MosaicMode.Pixelate;

        // 缓存马赛克位图，仅当参数变化时重建
        private BitmapSource _cachedBitmap;
        private Rect _cachedBounds;
        private int _cachedBlockSize;
        private MosaicMode _cachedMode;
        private WeakReference<BitmapSource> _cachedSourceRef;

        public override Rect GetBounds() => Bounds;

        public override void Draw(DrawingContext dc, bool selected, BitmapSource sourceImage)
        {
            if (sourceImage != null)
            {
                var bitmap = GetOrBuildEffectBitmap(sourceImage);
                if (bitmap != null) dc.DrawImage(bitmap, Bounds);
            }
            if (selected) DrawSelectionAdorner(dc, Bounds);
        }

        private BitmapSource GetOrBuildEffectBitmap(BitmapSource sourceImage)
        {
            // 检查缓存是否仍然有效
            if (_cachedBitmap != null &&
                _cachedSourceRef != null &&
                _cachedSourceRef.TryGetTarget(out var cachedSource) &&
                ReferenceEquals(cachedSource, sourceImage) &&
                _cachedBounds == Bounds &&
                _cachedBlockSize == BlockSize &&
                _cachedMode == Mode)
            {
                return _cachedBitmap;
            }

            _cachedBitmap = BuildEffectBitmap(sourceImage);
            _cachedBounds = Bounds;
            _cachedBlockSize = BlockSize;
            _cachedMode = Mode;
            if (_cachedSourceRef == null)
                _cachedSourceRef = new WeakReference<BitmapSource>(sourceImage);
            else
                _cachedSourceRef.SetTarget(sourceImage);
            return _cachedBitmap;
        }

        private BitmapSource BuildEffectBitmap(BitmapSource sourceImage)
        {
            int x = Math.Max(0, (int)Math.Round(Bounds.X));
            int y = Math.Max(0, (int)Math.Round(Bounds.Y));
            int w = Math.Min((int)Math.Round(Bounds.Width), sourceImage.PixelWidth - x);
            int h = Math.Min((int)Math.Round(Bounds.Height), sourceImage.PixelHeight - y);
            if (w <= 0 || h <= 0) return null;

            var converted = new FormatConvertedBitmap(sourceImage, PixelFormats.Bgra32, null, 0);
            int stride = w * 4;
            var pixels = new byte[h * stride];
            converted.CopyPixels(new Int32Rect(x, y, w, h), pixels, stride, 0);

            if (Mode == MosaicMode.Blur)
                ApplyBlur(pixels, w, h, stride, Math.Max(1, BlockSize / 2));
            else
                ApplyPixelate(pixels, w, h, stride, Math.Max(2, BlockSize));

            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            bmp.Freeze();
            return bmp;
        }

        private static void ApplyPixelate(byte[] pixels, int w, int h, int stride, int block)
        {
            for (int by = 0; by < h; by += block)
            {
                int bh = Math.Min(block, h - by);
                for (int bx = 0; bx < w; bx += block)
                {
                    int bw = Math.Min(block, w - bx);
                    long sumB = 0, sumG = 0, sumR = 0, sumA = 0;
                    int count = 0;
                    for (int j = 0; j < bh; j++)
                    {
                        int rowOffset = (by + j) * stride + bx * 4;
                        for (int i = 0; i < bw; i++)
                        {
                            int idx = rowOffset + i * 4;
                            sumB += pixels[idx]; sumG += pixels[idx + 1]; sumR += pixels[idx + 2]; sumA += pixels[idx + 3];
                            count++;
                        }
                    }
                    byte avgB = (byte)(sumB / count), avgG = (byte)(sumG / count), avgR = (byte)(sumR / count), avgA = (byte)(sumA / count);
                    for (int j = 0; j < bh; j++)
                    {
                        int rowOffset = (by + j) * stride + bx * 4;
                        for (int i = 0; i < bw; i++)
                        {
                            int idx = rowOffset + i * 4;
                            pixels[idx] = avgB; pixels[idx + 1] = avgG; pixels[idx + 2] = avgR; pixels[idx + 3] = avgA;
                        }
                    }
                }
            }
        }

        private static void ApplyBlur(byte[] pixels, int w, int h, int stride, int radius)
        {
            radius = Math.Max(1, Math.Min(radius, 18));
            for (int pass = 0; pass < 3; pass++)
            {
                BoxBlurHorizontal(pixels, w, h, stride, radius);
                BoxBlurVertical(pixels, w, h, stride, radius);
            }
        }

        private static void BoxBlurHorizontal(byte[] pixels, int w, int h, int stride, int radius)
        {
            var copy = (byte[])pixels.Clone();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int count = 0;
                    int sumB = 0, sumG = 0, sumR = 0, sumA = 0;
                    int from = Math.Max(0, x - radius);
                    int to = Math.Min(w - 1, x + radius);
                    for (int sx = from; sx <= to; sx++)
                    {
                        int source = y * stride + sx * 4;
                        sumB += copy[source];
                        sumG += copy[source + 1];
                        sumR += copy[source + 2];
                        sumA += copy[source + 3];
                        count++;
                    }
                    int target = y * stride + x * 4;
                    pixels[target] = (byte)(sumB / count);
                    pixels[target + 1] = (byte)(sumG / count);
                    pixels[target + 2] = (byte)(sumR / count);
                    pixels[target + 3] = (byte)(sumA / count);
                }
            }
        }

        private static void BoxBlurVertical(byte[] pixels, int w, int h, int stride, int radius)
        {
            var copy = (byte[])pixels.Clone();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int count = 0;
                    int sumB = 0, sumG = 0, sumR = 0, sumA = 0;
                    int from = Math.Max(0, y - radius);
                    int to = Math.Min(h - 1, y + radius);
                    for (int sy = from; sy <= to; sy++)
                    {
                        int source = sy * stride + x * 4;
                        sumB += copy[source];
                        sumG += copy[source + 1];
                        sumR += copy[source + 2];
                        sumA += copy[source + 3];
                        count++;
                    }
                    int target = y * stride + x * 4;
                    pixels[target] = (byte)(sumB / count);
                    pixels[target + 1] = (byte)(sumG / count);
                    pixels[target + 2] = (byte)(sumR / count);
                    pixels[target + 3] = (byte)(sumA / count);
                }
            }
        }

        public override void Move(Vector delta) => Bounds = new Rect(Bounds.TopLeft + delta, Bounds.Size);

        public override Annotation Clone() => new MosaicAnnotation { Bounds = Bounds, BlockSize = BlockSize, Mode = Mode, StrokeColor = StrokeColor, Thickness = Thickness };
    }

    public class TextAnnotation : Annotation
    {
        public Point Location { get; set; }
        public string Text { get; set; } = string.Empty;
        public double FontSize { get; set; } = 36;

        private FormattedText BuildFormattedText()
        {
            var brush = new SolidColorBrush(StrokeColor);
            brush.Freeze();
            return new FormattedText(
                Text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily(AnnotationConstants.DefaultFontFamily), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                FontSize,
                brush,
                1.0);
        }

        public override Rect GetBounds()
        {
            var ft = BuildFormattedText();
            return new Rect(Location, new Size(Math.Max(ft.Width, 1), Math.Max(ft.Height, 1)));
        }

        public override void Draw(DrawingContext dc, bool selected, BitmapSource sourceImage)
        {
            var ft = BuildFormattedText();
            dc.DrawText(ft, Location);
            if (selected) DrawSelectionAdorner(dc, new Rect(Location, new Size(ft.Width, ft.Height)));
        }

        public override void Move(Vector delta) => Location += delta;

        public override Annotation Clone() => new TextAnnotation { Location = Location, Text = Text, StrokeColor = StrokeColor, Thickness = Thickness, FontSize = FontSize };
    }
}
