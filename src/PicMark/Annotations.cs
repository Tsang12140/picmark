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
        public const string DefaultFontFamily = "Alibaba PuHuiTi 3.0, Alibaba PuHuiTi, Microsoft YaHei UI, Microsoft YaHei, SimHei";
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

    public class OrganicRectAnnotation : Annotation
    {
        public Rect Bounds { get; set; }
        public List<Point> Points { get; } = new List<Point>();
        private StreamGeometry _geometry;

        public override Rect GetBounds() => Bounds;

        public override void Draw(DrawingContext dc, bool selected, BitmapSource sourceImage)
        {
            if (Points.Count < 4) return;
            var brush = new SolidColorBrush(StrokeColor);
            brush.Freeze();
            var pen = new Pen(brush, Thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();
            dc.DrawGeometry(null, pen, GetGeometry());
            if (selected) DrawSelectionAdorner(dc, Bounds);
        }

        private StreamGeometry GetGeometry()
        {
            if (_geometry != null) return _geometry;

            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                Point start = Midpoint(Points[Points.Count - 1], Points[0]);
                context.BeginFigure(start, false, true);
                for (int i = 0; i < Points.Count; i++)
                {
                    Point next = Points[(i + 1) % Points.Count];
                    context.QuadraticBezierTo(Points[i], Midpoint(Points[i], next), true, false);
                }
            }
            geometry.Freeze();
            _geometry = geometry;
            return geometry;
        }

        private static Point Midpoint(Point a, Point b) =>
            new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);

        public override void Move(Vector delta)
        {
            Bounds = new Rect(Bounds.TopLeft + delta, Bounds.Size);
            for (int i = 0; i < Points.Count; i++) Points[i] += delta;
            _geometry = null;
        }

        public override Annotation Clone()
        {
            var clone = new OrganicRectAnnotation
            {
                Bounds = Bounds,
                StrokeColor = StrokeColor,
                Thickness = Thickness
            };
            clone.Points.AddRange(Points);
            return clone;
        }
    }

    public class OrganicPolygonAnnotation : Annotation
    {
        public List<Point> Vertices { get; } = new List<Point>();
        private StreamGeometry _geometry;

        public override Rect GetBounds()
        {
            if (Vertices.Count == 0) return Rect.Empty;
            double left = Vertices.Min(point => point.X);
            double top = Vertices.Min(point => point.Y);
            double right = Vertices.Max(point => point.X);
            double bottom = Vertices.Max(point => point.Y);
            return new Rect(new Point(left, top), new Point(right, bottom));
        }

        public override void Draw(DrawingContext dc, bool selected, BitmapSource sourceImage)
        {
            if (Vertices.Count < 3) return;
            var brush = new SolidColorBrush(StrokeColor);
            brush.Freeze();
            var pen = new Pen(brush, Thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();
            dc.DrawGeometry(null, pen, GetGeometry());
            if (selected) DrawSelectionAdorner(dc, GetBounds());
        }

        private StreamGeometry GetGeometry()
        {
            if (_geometry != null) return _geometry;
            int count = Vertices.Count;
            var before = new Point[count];
            var after = new Point[count];

            for (int i = 0; i < count; i++)
            {
                Point previous = Vertices[(i - 1 + count) % count];
                Point current = Vertices[i];
                Point next = Vertices[(i + 1) % count];
                Vector incoming = previous - current;
                Vector outgoing = next - current;
                double radius = Math.Min(incoming.Length, outgoing.Length) * 0.085;
                if (incoming.Length > 0) incoming.Normalize();
                if (outgoing.Length > 0) outgoing.Normalize();
                before[i] = current + incoming * radius;
                after[i] = current + outgoing * radius;
            }

            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(after[count - 1], false, true);
                for (int i = 0; i < count; i++)
                {
                    context.LineTo(before[i], true, false);
                    context.QuadraticBezierTo(Vertices[i], after[i], true, false);
                }
            }
            geometry.Freeze();
            _geometry = geometry;
            return geometry;
        }

        public override void Move(Vector delta)
        {
            for (int i = 0; i < Vertices.Count; i++) Vertices[i] += delta;
            _geometry = null;
        }

        public override Annotation Clone()
        {
            var clone = new OrganicPolygonAnnotation
            {
                StrokeColor = StrokeColor,
                Thickness = Thickness
            };
            clone.Vertices.AddRange(Vertices);
            return clone;
        }
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

    public class OpenEllipseAnnotation : Annotation
    {
        public Rect Bounds { get; set; }
        public double StartAngle { get; set; }
        public double SweepAngle { get; set; }
        public List<Point> Points { get; } = new List<Point>();

        public override Rect GetBounds() => Bounds;

        public override void Draw(DrawingContext dc, bool selected, BitmapSource sourceImage)
        {
            if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
            var contour = Points.Count >= 2 ? Points : BuildFallbackPoints();
            if (contour.Count < 2) return;

            var brush = new SolidColorBrush(StrokeColor);
            brush.Freeze();
            int taperSegments = Math.Min(9, Math.Max(3, contour.Count / 8));
            int bodyStart = taperSegments;
            int bodyEnd = contour.Count - taperSegments - 1;

            if (bodyEnd > bodyStart)
            {
                var bodyPen = new Pen(brush, Thickness)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                bodyPen.Freeze();
                var body = new StreamGeometry();
                using (var context = body.Open())
                {
                    context.BeginFigure(contour[bodyStart], false, false);
                    for (int i = bodyStart + 1; i <= bodyEnd; i++)
                        context.LineTo(contour[i], true, false);
                }
                body.Freeze();
                dc.DrawGeometry(null, bodyPen, body);
            }

            DrawTaper(dc, brush, contour, 0, taperSegments, true);
            DrawTaper(dc, brush, contour, contour.Count - taperSegments - 1, contour.Count - 1, false);
            if (selected) DrawSelectionAdorner(dc, Bounds);
        }

        private void DrawTaper(DrawingContext dc, Brush brush, IList<Point> contour, int from, int to, bool grow)
        {
            int count = Math.Max(1, to - from);
            for (int i = from; i < to; i++)
            {
                double progress = (i - from + 1.0) / count;
                double factor = grow ? progress : 1.0 - (i - from) / (double)count;
                factor = 0.16 + 0.84 * Math.Pow(Math.Max(0, factor), 0.72);
                var pen = new Pen(brush, Math.Max(0.8, Thickness * factor))
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                pen.Freeze();
                dc.DrawLine(pen, contour[i], contour[i + 1]);
            }
        }

        private List<Point> BuildFallbackPoints()
        {
            var result = new List<Point>();
            if (Math.Abs(SweepAngle) < 0.01) return result;
            var center = new Point(Bounds.X + Bounds.Width / 2, Bounds.Y + Bounds.Height / 2);
            double rx = Bounds.Width / 2;
            double ry = Bounds.Height / 2;
            int segments = Math.Max(24, (int)(Math.Abs(SweepAngle) / (Math.PI * 2) * 96));
            for (int i = 0; i <= segments; i++)
            {
                double angle = StartAngle + SweepAngle * i / segments;
                result.Add(new Point(center.X + Math.Cos(angle) * rx, center.Y + Math.Sin(angle) * ry));
            }
            return result;
        }

        public override void Move(Vector delta)
        {
            Bounds = new Rect(Bounds.TopLeft + delta, Bounds.Size);
            for (int i = 0; i < Points.Count; i++) Points[i] += delta;
        }

        public override Annotation Clone()
        {
            var clone = new OpenEllipseAnnotation
            {
                Bounds = Bounds,
                StartAngle = StartAngle,
                SweepAngle = SweepAngle,
                StrokeColor = StrokeColor,
                Thickness = Thickness
            };
            clone.Points.AddRange(Points);
            return clone;
        }
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
        // 旋转角度（度）。Bounds 始终保存未旋转时的本地尺寸，便于继续编辑。
        public double Angle { get; set; }

        // 缓存马赛克位图，仅当参数变化时重建
        private BitmapSource _cachedBitmap;
        private Rect _cachedEffectBounds;
        private int _cachedBlockSize;
        private MosaicMode _cachedMode;
        private WeakReference<BitmapSource> _cachedSourceRef;
        private bool _isTransforming;
        // 拖动时只计算当前区域的缩小预览；像素马赛克和高斯模糊分别设限，
        // 避免大图上的每次鼠标移动都触发百万级像素运算。
        private const int InteractivePixelatePreviewPixels = 180000;
        private const int InteractiveGaussianPreviewPixels = 90000;
        private static readonly Dictionary<int, double[]> GaussianWeights = new Dictionary<int, double[]>();

        public override Rect GetBounds()
        {
            if (Bounds.IsEmpty || Math.Abs(Angle) < 0.001) return Bounds;

            double radians = Angle * Math.PI / 180.0;
            double cos = Math.Abs(Math.Cos(radians));
            double sin = Math.Abs(Math.Sin(radians));
            double width = Bounds.Width * cos + Bounds.Height * sin;
            double height = Bounds.Width * sin + Bounds.Height * cos;
            var center = GetCenter();
            return new Rect(center.X - width / 2, center.Y - height / 2, width, height);
        }

        public Point GetCenter() => new Point(Bounds.Left + Bounds.Width / 2, Bounds.Top + Bounds.Height / 2);

        public bool Contains(Point point)
        {
            Point local = RotatePoint(point, GetCenter(), -Angle);
            return Bounds.Contains(local);
        }

        public Point[] GetCorners()
        {
            var center = GetCenter();
            return new[]
            {
                RotatePoint(new Point(Bounds.Left, Bounds.Top), center, Angle),
                RotatePoint(new Point(Bounds.Right, Bounds.Top), center, Angle),
                RotatePoint(new Point(Bounds.Right, Bounds.Bottom), center, Angle),
                RotatePoint(new Point(Bounds.Left, Bounds.Bottom), center, Angle)
            };
        }

        public void SetInteractiveTransforming(bool value)
        {
            if (_isTransforming == value) return;
            _isTransforming = value;
            // 松开鼠标后清掉缩略预览，立即按原图分辨率精确重算。
            if (!value) ClearEffectCache();
        }

        public override void Draw(DrawingContext dc, bool selected, BitmapSource sourceImage)
        {
            if (sourceImage != null)
            {
                Rect effectBounds = GetEffectBounds(sourceImage);
                var bitmap = GetOrBuildEffectBitmap(sourceImage, effectBounds);
                if (bitmap != null)
                {
                    var clip = new RectangleGeometry(Bounds)
                    {
                        Transform = new RotateTransform(Angle, GetCenter().X, GetCenter().Y)
                    };
                    dc.PushClip(clip);
                    dc.DrawImage(bitmap, effectBounds);
                    dc.Pop();
                }
            }
            if (selected) DrawSelectionAdorner(dc, GetBounds());
        }

        private Rect GetEffectBounds(BitmapSource sourceImage)
        {
            Rect visualBounds = GetBounds();
            int left = Math.Max(0, (int)Math.Floor(visualBounds.Left));
            int top = Math.Max(0, (int)Math.Floor(visualBounds.Top));
            int right = Math.Min(sourceImage.PixelWidth, (int)Math.Ceiling(visualBounds.Right));
            int bottom = Math.Min(sourceImage.PixelHeight, (int)Math.Ceiling(visualBounds.Bottom));
            return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }

        private BitmapSource GetOrBuildEffectBitmap(BitmapSource sourceImage, Rect effectBounds)
        {
            bool isCachedSource = _cachedSourceRef != null &&
                _cachedSourceRef.TryGetTarget(out var cachedSource) &&
                ReferenceEquals(cachedSource, sourceImage);
            if (_cachedBitmap != null && isCachedSource &&
                _cachedEffectBounds == effectBounds &&
                _cachedBlockSize == BlockSize &&
                _cachedMode == Mode)
            {
                return _cachedBitmap;
            }

            _cachedBitmap = BuildEffectBitmap(sourceImage, effectBounds, _isTransforming);
            _cachedEffectBounds = effectBounds;
            _cachedBlockSize = BlockSize;
            _cachedMode = Mode;
            if (_cachedSourceRef == null)
                _cachedSourceRef = new WeakReference<BitmapSource>(sourceImage);
            else
                _cachedSourceRef.SetTarget(sourceImage);
            return _cachedBitmap;
        }

        private void ClearEffectCache()
        {
            _cachedBitmap = null;
            _cachedEffectBounds = Rect.Empty;
            _cachedSourceRef = null;
        }

        private BitmapSource BuildEffectBitmap(BitmapSource sourceImage, Rect effectBounds, bool interactive)
        {
            int x = (int)effectBounds.X;
            int y = (int)effectBounds.Y;
            int w = (int)effectBounds.Width;
            int h = (int)effectBounds.Height;
            if (w <= 0 || h <= 0) return null;

            double sampleScale = 1.0;
            long sourcePixels = (long)w * h;
            int previewLimit = Mode == MosaicMode.Blur
                ? InteractiveGaussianPreviewPixels
                : InteractivePixelatePreviewPixels;
            if (interactive && sourcePixels > previewLimit)
                sampleScale = Math.Sqrt(previewLimit / (double)sourcePixels);

            BitmapSource sampled = new CroppedBitmap(sourceImage, new Int32Rect(x, y, w, h));
            if (sampleScale < 0.999)
                sampled = new TransformedBitmap(sampled, new ScaleTransform(sampleScale, sampleScale));

            var converted = new FormatConvertedBitmap(sampled, PixelFormats.Bgra32, null, 0);
            int sampleWidth = converted.PixelWidth;
            int sampleHeight = converted.PixelHeight;
            int stride = sampleWidth * 4;
            var pixels = new byte[sampleHeight * stride];
            converted.CopyPixels(pixels, stride, 0);

            if (Mode == MosaicMode.Blur)
                ApplyGaussianBlur(pixels, sampleWidth, sampleHeight, stride, Math.Max(1, (int)Math.Round(BlockSize * sampleScale / 2.0)));
            else
                ApplyPixelate(pixels, sampleWidth, sampleHeight, stride, Math.Max(2, (int)Math.Round(BlockSize * sampleScale)));

            var bmp = BitmapSource.Create(sampleWidth, sampleHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
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

        private static void ApplyGaussianBlur(byte[] pixels, int w, int h, int stride, int radius)
        {
            radius = Math.Max(1, Math.Min(radius, 15));
            double[] weights = GetGaussianWeights(radius);
            var horizontal = new byte[pixels.Length];

            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int x = 0; x < w; x++)
                {
                    double blue = 0, green = 0, red = 0, total = 0;
                    int from = Math.Max(0, x - radius);
                    int to = Math.Min(w - 1, x + radius);
                    for (int sampleX = from; sampleX <= to; sampleX++)
                    {
                        double weight = weights[sampleX - x + radius];
                        int source = row + sampleX * 4;
                        blue += pixels[source] * weight;
                        green += pixels[source + 1] * weight;
                        red += pixels[source + 2] * weight;
                        total += weight;
                    }
                    int target = row + x * 4;
                    horizontal[target] = (byte)(blue / total);
                    horizontal[target + 1] = (byte)(green / total);
                    horizontal[target + 2] = (byte)(red / total);
                    horizontal[target + 3] = pixels[target + 3];
                }
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double blue = 0, green = 0, red = 0, total = 0;
                    int from = Math.Max(0, y - radius);
                    int to = Math.Min(h - 1, y + radius);
                    for (int sampleY = from; sampleY <= to; sampleY++)
                    {
                        double weight = weights[sampleY - y + radius];
                        int source = sampleY * stride + x * 4;
                        blue += horizontal[source] * weight;
                        green += horizontal[source + 1] * weight;
                        red += horizontal[source + 2] * weight;
                        total += weight;
                    }
                    int target = y * stride + x * 4;
                    pixels[target] = (byte)(blue / total);
                    pixels[target + 1] = (byte)(green / total);
                    pixels[target + 2] = (byte)(red / total);
                    pixels[target + 3] = horizontal[target + 3];
                }
            }
        }

        private static double[] GetGaussianWeights(int radius)
        {
            if (GaussianWeights.TryGetValue(radius, out var cached)) return cached;

            double sigma = Math.Max(1.0, radius * 0.55);
            var weights = new double[radius * 2 + 1];
            double sum = 0;
            for (int i = -radius; i <= radius; i++)
            {
                double weight = Math.Exp(-(i * i) / (2 * sigma * sigma));
                weights[i + radius] = weight;
                sum += weight;
            }
            for (int i = 0; i < weights.Length; i++) weights[i] /= sum;
            GaussianWeights[radius] = weights;
            return weights;
        }

        public override void Move(Vector delta) => Bounds = new Rect(Bounds.TopLeft + delta, Bounds.Size);

        public override Annotation Clone() => new MosaicAnnotation { Bounds = Bounds, BlockSize = BlockSize, Mode = Mode, Angle = Angle, StrokeColor = StrokeColor, Thickness = Thickness };

        private static Point RotatePoint(Point point, Point center, double angle)
        {
            double radians = angle * Math.PI / 180.0;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);
            double dx = point.X - center.X;
            double dy = point.Y - center.Y;
            return new Point(center.X + dx * cos - dy * sin, center.Y + dx * sin + dy * cos);
        }
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
