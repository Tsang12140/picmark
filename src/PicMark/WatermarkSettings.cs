using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PicMark
{
    public enum WatermarkStyle
    {
        DiamondGrid,
        TextOnly
    }

    public enum WatermarkLayout
    {
        Tiled,
        Single
    }

    public sealed class WatermarkSettings
    {
        public bool Enabled { get; set; } = true;
        public string Text { get; set; } = string.Empty;
        public WatermarkStyle Style { get; set; } = WatermarkStyle.DiamondGrid;
        public double Opacity { get; set; } = 0.2;
        public double FontSize { get; set; } = 28;
        public double Spacing { get; set; } = 204;
        public double HorizontalOffset { get; set; }
        public double VerticalOffset { get; set; }
        public double Angle { get; set; }
        public bool Bold { get; set; }
        public string FontFamilyName { get; set; } = "Microsoft YaHei UI";
        public WatermarkLayout Layout { get; set; } = WatermarkLayout.Tiled;
        public Color Color { get; set; } = Colors.Black;

        public WatermarkSettings Clone()
        {
            return new WatermarkSettings
            {
                Enabled = Enabled,
                Text = Text,
                Style = Style,
                Opacity = Opacity,
                FontSize = FontSize,
                Spacing = Spacing,
                HorizontalOffset = HorizontalOffset,
                VerticalOffset = VerticalOffset,
                Angle = Angle,
                Bold = Bold,
                FontFamilyName = FontFamilyName,
                Layout = Layout,
                Color = Color
            };
        }

        public void Draw(DrawingContext dc, BitmapSource sourceImage)
        {
            if (!Enabled || sourceImage == null || string.IsNullOrWhiteSpace(Text)) return;

            double spacing = Math.Max(80, Math.Min(400, Spacing));
            double cellWidth = spacing * 1.8;
            double cellHeight = cellWidth * 0.72;
            double width = sourceImage.PixelWidth;
            double height = sourceImage.PixelHeight;

            dc.PushOpacity(Math.Max(0.03, Math.Min(0.8, Opacity)));
            if (Layout == WatermarkLayout.Single)
                DrawSingle(dc, width, height, cellWidth, cellHeight);
            else if (Style == WatermarkStyle.DiamondGrid)
                DrawDiamondGrid(dc, width, height, cellWidth, cellHeight);
            else
                DrawTextPattern(dc, width, height, cellWidth, cellHeight);
            dc.Pop();
        }

        private void DrawDiamondGrid(DrawingContext dc, double width, double height, double cellWidth, double cellHeight)
        {
            var brush = new SolidColorBrush(Color);
            var pen = new Pen(brush, Math.Max(0.7, FontSize / 34))
            {
                DashStyle = new DashStyle(new[] { 4.0, 4.0 }, 0)
            };
            pen.Freeze();

            double horizontalStep = cellWidth;
            double verticalStep = cellHeight;
            double halfWidth = horizontalStep / 2;
            double halfHeight = verticalStep / 2;
            double originX = NormalizeOffset(HorizontalOffset, horizontalStep);
            double originY = NormalizeOffset(VerticalOffset, verticalStep);
            int rowStart = -2;
            int rowEnd = (int)Math.Ceiling((height - originY) / verticalStep) + 2;
            int columnStart = -2;
            int columnEnd = (int)Math.Ceiling((width - originX) / horizontalStep) + 2;

            for (int row = rowStart; row <= rowEnd; row++)
            {
                double centerY = originY + row * verticalStep;
                for (int column = columnStart; column <= columnEnd; column++)
                {
                    double centerX = originX + column * horizontalStep;
                    var center = new Point(centerX, centerY);
                    var topLeft = new Point(centerX - halfWidth, centerY - halfHeight);
                    var topRight = new Point(centerX + halfWidth, centerY - halfHeight);
                    var bottomRight = new Point(centerX + halfWidth, centerY + halfHeight);
                    var bottomLeft = new Point(centerX - halfWidth, centerY + halfHeight);

                    string[] lines = BuildDisplayText(Text).Split('\n');
                    int longestLine = lines.Length == 0 ? 1 : lines.Max(line => line.Length);
                    double textHalfWidth = Math.Min(cellWidth * 0.31, Math.Max(44, longestLine * FontSize * 0.28));
                    double textHalfHeight = Math.Max(FontSize * 0.7, lines.Length * FontSize * 0.68);

                    DrawRayOutsideText(dc, pen, center, topLeft, textHalfWidth, textHalfHeight);
                    DrawRayOutsideText(dc, pen, center, topRight, textHalfWidth, textHalfHeight);
                    DrawRayOutsideText(dc, pen, center, bottomRight, textHalfWidth, textHalfHeight);
                    DrawRayOutsideText(dc, pen, center, bottomLeft, textHalfWidth, textHalfHeight);

                    if (IsSafelyInside(center, width, height, textHalfWidth, textHalfHeight))
                        DrawCenteredText(dc, center, cellWidth * 0.54, Angle);

                    if (IsSafelyInside(bottomRight, width, height, 10, 10))
                        dc.DrawEllipse(brush, null, bottomRight, Math.Max(2.2, FontSize * 0.09), Math.Max(2.2, FontSize * 0.09));
                }
            }
        }

        private static double NormalizeOffset(double offset, double step)
        {
            if (step <= 0) return offset;
            double normalized = offset % step;
            return normalized < 0 ? normalized + step : normalized;
        }

        private static bool IsSafelyInside(Point center, double width, double height, double halfWidth, double halfHeight)
        {
            return center.X - halfWidth >= 8 &&
                   center.X + halfWidth <= width - 8 &&
                   center.Y - halfHeight >= 8 &&
                   center.Y + halfHeight <= height - 8;
        }

        private static void DrawRayOutsideText(
            DrawingContext dc,
            Pen pen,
            Point center,
            Point target,
            double textHalfWidth,
            double textHalfHeight)
        {
            Vector delta = target - center;
            double xFraction = Math.Abs(delta.X) < 0.001 ? 0 : textHalfWidth / Math.Abs(delta.X);
            double yFraction = Math.Abs(delta.Y) < 0.001 ? 0 : textHalfHeight / Math.Abs(delta.Y);
            double boundaryFraction;
            if (xFraction <= 0) boundaryFraction = yFraction;
            else if (yFraction <= 0) boundaryFraction = xFraction;
            else boundaryFraction = Math.Min(xFraction, yFraction);
            double startFraction = Math.Min(0.78, boundaryFraction + 0.04);
            dc.DrawLine(pen, center + delta * startFraction, target);
        }

        private void DrawTextPattern(DrawingContext dc, double width, double height, double cellWidth, double cellHeight)
        {
            int rowEnd = (int)Math.Ceiling(height / cellHeight) + 2;
            int columnEnd = (int)Math.Ceiling(width / cellWidth) + 2;

            for (int row = -2; row <= rowEnd; row++)
            {
                double stagger = Math.Abs(row % 2) == 1 ? cellWidth / 2 : 0;
                for (int column = -2; column <= columnEnd; column++)
                {
                    var center = new Point(
                        column * cellWidth + stagger + HorizontalOffset,
                        row * cellHeight + cellHeight / 2);
                    DrawCenteredText(dc, center, cellWidth * 0.82, Angle);
                }
            }
        }

        private void DrawSingle(DrawingContext dc, double width, double height, double cellWidth, double cellHeight)
        {
            var center = new Point(width / 2 + HorizontalOffset, height / 2 + VerticalOffset);
            DrawCenteredText(dc, center, Math.Max(120, width * 0.8), Angle);
        }

        private void DrawCenteredText(DrawingContext dc, Point center, double maxWidth, double angle)
        {
            string displayText = BuildDisplayText(Text);
            double requestedSize = Math.Max(10, Math.Min(160, FontSize));
            var typeface = new Typeface(
                new FontFamily(string.IsNullOrWhiteSpace(FontFamilyName) ? "Microsoft YaHei UI" : FontFamilyName),
                FontStyles.Normal,
                Bold ? FontWeights.Bold : FontWeights.Normal,
                FontStretches.Normal);
            var brush = new SolidColorBrush(Color);
            var formatted = new FormattedText(
                displayText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                requestedSize,
                brush,
                1.0)
            {
                TextAlignment = TextAlignment.Left,
                LineHeight = requestedSize * 1.28
            };

            if (formatted.Width > maxWidth)
            {
                double fittedSize = Math.Max(9, requestedSize * maxWidth / formatted.Width);
                formatted = new FormattedText(
                    displayText,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fittedSize,
                    brush,
                    1.0)
                {
                    TextAlignment = TextAlignment.Left,
                    LineHeight = fittedSize * 1.28
                };
            }

            if (Math.Abs(angle) > 0.01)
                dc.PushTransform(new RotateTransform(angle, center.X, center.Y));
            dc.DrawText(formatted, new Point(center.X - formatted.Width / 2, center.Y - formatted.Height / 2));
            if (Math.Abs(angle) > 0.01)
                dc.Pop();
        }

        private static string BuildDisplayText(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        }

        public bool HitTestSingle(Point point, BitmapSource sourceImage)
        {
            if (!Enabled || Layout != WatermarkLayout.Single || sourceImage == null) return false;
            double lineCount = Math.Max(1, BuildDisplayText(Text).Split('\n').Length);
            double halfWidth = Math.Min(sourceImage.PixelWidth * 0.42, Math.Max(100, Text.Length * FontSize * 0.36));
            double halfHeight = Math.Max(34, FontSize * lineCount * 0.8);
            var center = new Point(
                sourceImage.PixelWidth / 2.0 + HorizontalOffset,
                sourceImage.PixelHeight / 2.0 + VerticalOffset);
            return new Rect(
                center.X - halfWidth,
                center.Y - halfHeight,
                halfWidth * 2,
                halfHeight * 2).Contains(point);
        }
    }
}
