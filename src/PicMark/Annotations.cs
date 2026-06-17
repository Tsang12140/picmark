using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PicMark
{
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

        public override Rect GetBounds() => new Rect(Start, End);

        public override void Draw(DrawingContext dc, bool selected, BitmapSource sourceImage)
        {
            var brush = new SolidColorBrush(StrokeColor);
            var pen = new Pen(brush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(pen, Start, End);

            var dir = End - Start;
            if (dir.Length > 0.001)
            {
                dir.Normalize();
                double headLength = Math.Max(14, Thickness * 3.2);
                double headWidth = Math.Max(8, Thickness * 2.2);
                var normal = new Vector(-dir.Y, dir.X);
                var baseCenter = End - dir * headLength;
                var p1 = baseCenter + normal * (headWidth / 2);
                var p2 = baseCenter - normal * (headWidth / 2);

                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(End, true, true);
                    ctx.LineTo(p1, true, true);
                    ctx.LineTo(p2, true, true);
                }
                geo.Freeze();
                dc.DrawGeometry(brush, null, geo);
            }

            if (selected) DrawSelectionAdorner(dc, GetBounds());
        }

        public override void Move(Vector delta)
        {
            Start += delta;
            End += delta;
        }

        public override Annotation Clone() => new ArrowAnnotation { Start = Start, End = End, StrokeColor = StrokeColor, Thickness = Thickness };
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
            var pen = new Pen(new SolidColorBrush(StrokeColor), Thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(Points[0], false, false);
                ctx.PolyLineTo(Points.Skip(1).ToList(), true, true);
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

        public override Rect GetBounds() => Bounds;

        public override void Draw(DrawingContext dc, bool selected, BitmapSource sourceImage)
        {
            if (sourceImage != null)
            {
                var pixelated = BuildPixelatedBitmap(sourceImage);
                if (pixelated != null) dc.DrawImage(pixelated, Bounds);
            }
            if (selected) DrawSelectionAdorner(dc, Bounds);
        }

        private BitmapSource BuildPixelatedBitmap(BitmapSource sourceImage)
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

            int block = Math.Max(2, BlockSize);
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

            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            bmp.Freeze();
            return bmp;
        }

        public override void Move(Vector delta) => Bounds = new Rect(Bounds.TopLeft + delta, Bounds.Size);

        public override Annotation Clone() => new MosaicAnnotation { Bounds = Bounds, BlockSize = BlockSize, StrokeColor = StrokeColor, Thickness = Thickness };
    }

    public class TextAnnotation : Annotation
    {
        public Point Location { get; set; }
        public string Text { get; set; } = string.Empty;
        public double FontSize { get; set; } = 36;

        private FormattedText BuildFormattedText()
        {
            return new FormattedText(
                Text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Microsoft YaHei"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                FontSize,
                new SolidColorBrush(StrokeColor),
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
