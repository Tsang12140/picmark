using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PicMark
{
    public enum WatermarkStyle
    {
        DiamondGrid,
        TextOnly,
        ImageLogo
    }

    public enum WatermarkLayout
    {
        Tiled,
        Single
    }

    public sealed class WatermarkSettings
    {
        private const double ReferenceShortEdge = 1080.0;
        private const double MinimumAdaptiveScale = 0.6;
        private const double MaximumAdaptiveScale = 6.0;
        private static readonly Dictionary<string, BitmapSource> LogoCache =
            new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);

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
        public string LogoPath { get; set; } = string.Empty;
        public double LogoScalePercent { get; set; } = 18;
        public bool LogoFlipHorizontal { get; set; }
        public bool LogoFlipVertical { get; set; }

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
                Color = Color,
                LogoPath = LogoPath,
                LogoScalePercent = LogoScalePercent,
                LogoFlipHorizontal = LogoFlipHorizontal,
                LogoFlipVertical = LogoFlipVertical
            };
        }

        public void Draw(DrawingContext dc, BitmapSource sourceImage)
        {
            if (!Enabled || sourceImage == null) return;
            if (Style == WatermarkStyle.ImageLogo)
            {
                DrawImageLogo(dc, sourceImage);
                return;
            }
            if (string.IsNullOrWhiteSpace(Text)) return;

            double adaptiveScale = GetAdaptiveScale(sourceImage);
            double effectiveFontSize = Math.Max(8, FontSize * adaptiveScale);
            double spacing = Math.Max(48, Spacing * adaptiveScale);
            string[] lines = BuildDisplayText(Text).Split('\n');
            int longestLine = Math.Max(1, lines.Max(line => line.Length));
            double estimatedTextWidth = Math.Max(effectiveFontSize * 2.2, longestLine * effectiveFontSize * 0.58);
            double estimatedTextHeight = Math.Max(effectiveFontSize * 1.28, lines.Length * effectiveFontSize * 1.28);
            double cellWidth = Math.Max(spacing * 1.8, estimatedTextWidth + spacing * 0.65);
            double cellHeight = Style == WatermarkStyle.DiamondGrid
                ? Math.Max(cellWidth * 0.72, estimatedTextHeight + spacing * 0.55)
                : Math.Max(spacing * 1.3, estimatedTextHeight + spacing * 0.45);
            double textPatternMaxWidth = estimatedTextWidth;
            if (Style == WatermarkStyle.TextOnly && Layout == WatermarkLayout.Tiled)
            {
                Size textSize = MeasureText(effectiveFontSize);
                double radians = Math.Abs(Angle) * Math.PI / 180.0;
                double rotatedWidth = Math.Abs(textSize.Width * Math.Cos(radians)) +
                                      Math.Abs(textSize.Height * Math.Sin(radians));
                double rotatedHeight = Math.Abs(textSize.Width * Math.Sin(radians)) +
                                       Math.Abs(textSize.Height * Math.Cos(radians));
                double safeGap = Math.Max(effectiveFontSize * 0.8, spacing * 0.28);
                cellWidth = Math.Max(spacing * 1.25, rotatedWidth + safeGap);
                cellHeight = Math.Max(spacing * 0.85, rotatedHeight + safeGap);
                textPatternMaxWidth = Math.Max(textSize.Width * 1.04, effectiveFontSize * 2.2);
            }
            double width = sourceImage.PixelWidth;
            double height = sourceImage.PixelHeight;

            dc.PushOpacity(Math.Max(0.03, Math.Min(0.8, Opacity)));
            if (Layout == WatermarkLayout.Single)
                DrawSingle(dc, width, height, effectiveFontSize);
            else if (Style == WatermarkStyle.DiamondGrid)
                DrawDiamondGrid(dc, width, height, cellWidth, cellHeight, effectiveFontSize, adaptiveScale);
            else
                DrawTextPattern(dc, width, height, cellWidth, cellHeight, effectiveFontSize, textPatternMaxWidth, adaptiveScale);
            dc.Pop();
        }

        private void DrawDiamondGrid(
            DrawingContext dc,
            double width,
            double height,
            double cellWidth,
            double cellHeight,
            double effectiveFontSize,
            double adaptiveScale)
        {
            var brush = new SolidColorBrush(Color);
            var pen = new Pen(brush, Math.Max(0.7, effectiveFontSize / 34))
            {
                DashStyle = new DashStyle(new[] { 4.0, 4.0 }, 0)
            };
            pen.Freeze();

            double halfWidth = cellWidth / 2;
            double halfHeight = cellHeight / 2;
            var center = new Point(halfWidth, halfHeight);
            var topLeft = new Point(0, 0);
            var topRight = new Point(cellWidth, 0);
            var bottomRight = new Point(cellWidth, cellHeight);
            var bottomLeft = new Point(0, cellHeight);
            string[] lines = BuildDisplayText(Text).Split('\n');
            int longestLine = lines.Length == 0 ? 1 : lines.Max(line => line.Length);
            double textHalfWidth = Math.Min(cellWidth * 0.31, Math.Max(44 * adaptiveScale, longestLine * effectiveFontSize * 0.28));
            double textHalfHeight = Math.Max(effectiveFontSize * 0.7, lines.Length * effectiveFontSize * 0.68);
            var formatted = CreateFittedText(effectiveFontSize, cellWidth * 0.72);

            var tile = new DrawingGroup();
            using (var tileDc = tile.Open())
            {
                DrawRayOutsideText(tileDc, pen, center, topLeft, textHalfWidth, textHalfHeight);
                DrawRayOutsideText(tileDc, pen, center, topRight, textHalfWidth, textHalfHeight);
                DrawRayOutsideText(tileDc, pen, center, bottomRight, textHalfWidth, textHalfHeight);
                DrawRayOutsideText(tileDc, pen, center, bottomLeft, textHalfWidth, textHalfHeight);
                DrawFormattedCentered(tileDc, center, Angle, formatted);
                double dotRadius = Math.Max(2.2 * adaptiveScale, effectiveFontSize * 0.09);
                tileDc.DrawEllipse(brush, null, bottomRight, dotRadius, dotRadius);
            }
            tile.Freeze();

            DrawTiledLayer(
                dc,
                tile,
                new Rect(0, 0, cellWidth, cellHeight),
                width,
                height,
                HorizontalOffset * adaptiveScale,
                VerticalOffset * adaptiveScale);
        }

        private static double NormalizeOffset(double offset, double step)
        {
            if (step <= 0) return offset;
            double normalized = offset % step;
            return normalized < 0 ? normalized + step : normalized;
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

        private void DrawTextPattern(
            DrawingContext dc,
            double width,
            double height,
            double cellWidth,
            double cellHeight,
            double effectiveFontSize,
            double textMaxWidth,
            double adaptiveScale)
        {
            double tileWidth = cellWidth * 2;
            double tileHeight = cellHeight * 2;
            var formatted = CreateFittedText(effectiveFontSize, textMaxWidth);
            var tile = new DrawingGroup();
            using (var tileDc = tile.Open())
            {
                DrawFormattedCentered(tileDc, new Point(cellWidth * 0.5, cellHeight * 0.5), Angle, formatted);
                DrawFormattedCentered(tileDc, new Point(cellWidth * 1.5, cellHeight * 0.5), Angle, formatted);
                DrawFormattedCentered(tileDc, new Point(0, cellHeight * 1.5), Angle, formatted);
                DrawFormattedCentered(tileDc, new Point(cellWidth, cellHeight * 1.5), Angle, formatted);
                DrawFormattedCentered(tileDc, new Point(tileWidth, cellHeight * 1.5), Angle, formatted);
            }
            tile.Freeze();

            DrawTiledLayer(
                dc,
                tile,
                new Rect(0, 0, tileWidth, tileHeight),
                width,
                height,
                HorizontalOffset * adaptiveScale,
                VerticalOffset * adaptiveScale);
        }

        private void DrawSingle(DrawingContext dc, double width, double height, double effectiveFontSize)
        {
            var center = new Point(width / 2 + HorizontalOffset, height / 2 + VerticalOffset);
            DrawCenteredText(dc, center, Math.Max(120, width * 0.8), Angle, effectiveFontSize);
        }

        private void DrawImageLogo(DrawingContext dc, BitmapSource sourceImage)
        {
            BitmapSource logo = LoadLogo(LogoPath);
            if (logo == null) return;

            Rect bounds = GetImageLogoBounds(sourceImage, logo);
            dc.PushOpacity(Math.Max(0.03, Math.Min(1.0, Opacity)));
            dc.PushTransform(new TranslateTransform(bounds.X + bounds.Width / 2.0, bounds.Y + bounds.Height / 2.0));
            if (Math.Abs(Angle) > 0.01)
                dc.PushTransform(new RotateTransform(Angle));
            dc.PushTransform(new ScaleTransform(LogoFlipHorizontal ? -1 : 1, LogoFlipVertical ? -1 : 1));
            dc.DrawImage(logo, new Rect(-bounds.Width / 2.0, -bounds.Height / 2.0, bounds.Width, bounds.Height));
            dc.Pop();
            if (Math.Abs(Angle) > 0.01)
                dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        private void DrawCenteredText(DrawingContext dc, Point center, double maxWidth, double angle, double effectiveFontSize)
        {
            DrawFormattedCentered(dc, center, angle, CreateFittedText(effectiveFontSize, maxWidth));
        }

        private FormattedText CreateFittedText(double effectiveFontSize, double maxWidth)
        {
            double requestedSize = Math.Max(8, Math.Min(960, effectiveFontSize));
            var formatted = CreateFormattedText(requestedSize);
            if (formatted.Width <= maxWidth) return formatted;
            double fittedSize = Math.Max(9, requestedSize * maxWidth / formatted.Width);
            return CreateFormattedText(fittedSize);
        }

        private static void DrawFormattedCentered(DrawingContext dc, Point center, double angle, FormattedText formatted)
        {
            if (Math.Abs(angle) > 0.01)
                dc.PushTransform(new RotateTransform(angle, center.X, center.Y));
            dc.DrawText(formatted, new Point(center.X - formatted.Width / 2, center.Y - formatted.Height / 2));
            if (Math.Abs(angle) > 0.01)
                dc.Pop();
        }

        private static void DrawTiledLayer(
            DrawingContext dc,
            Drawing drawing,
            Rect tileBounds,
            double width,
            double height,
            double offsetX,
            double offsetY)
        {
            var tileBrush = new DrawingBrush(drawing)
            {
                TileMode = TileMode.Tile,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = tileBounds,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(
                    NormalizeOffset(offsetX, tileBounds.Width) - tileBounds.Width,
                    NormalizeOffset(offsetY, tileBounds.Height) - tileBounds.Height,
                    tileBounds.Width,
                    tileBounds.Height),
                Stretch = Stretch.Fill
            };
            tileBrush.Freeze();
            dc.DrawRectangle(tileBrush, null, new Rect(0, 0, width, height));
        }

        private Size MeasureText(double effectiveFontSize)
        {
            var formatted = CreateFormattedText(Math.Max(8, Math.Min(960, effectiveFontSize)));
            return new Size(Math.Max(1, formatted.Width), Math.Max(1, formatted.Height));
        }

        private FormattedText CreateFormattedText(double fontSize)
        {
            var typeface = new Typeface(
                new FontFamily(string.IsNullOrWhiteSpace(FontFamilyName) ? "Microsoft YaHei UI" : FontFamilyName),
                FontStyles.Normal,
                Bold ? FontWeights.Bold : FontWeights.Normal,
                FontStretches.Normal);
            var formatted = new FormattedText(
                BuildDisplayText(Text),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                new SolidColorBrush(Color),
                1.0)
            {
                TextAlignment = TextAlignment.Left,
                LineHeight = fontSize * 1.28
            };
            return formatted;
        }

        private static string BuildDisplayText(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        }

        public bool HitTestSingle(Point point, BitmapSource sourceImage) => HitTestSingle(point, sourceImage, 1.0);

        public bool HitTestSingle(Point point, BitmapSource sourceImage, double renderScale)
        {
            if (!Enabled || sourceImage == null) return false;
            if (Style != WatermarkStyle.ImageLogo && Layout != WatermarkLayout.Single) return false;
            if (Style == WatermarkStyle.ImageLogo)
            {
                BitmapSource logo = LoadLogo(LogoPath);
                if (logo == null) return false;
                Rect hitBounds = GetImageLogoBounds(sourceImage, logo);
                double scale = Math.Max(0.05, renderScale);
                double screenPadding = 28 / scale;
                double padding = Math.Max(screenPadding, Math.Max(hitBounds.Width, hitBounds.Height) / 6.0);
                hitBounds.Inflate(padding, padding);
                return hitBounds.Contains(point);
            }
            double effectiveFontSize = FontSize * GetAdaptiveScale(sourceImage);
            double lineCount = Math.Max(1, BuildDisplayText(Text).Split('\n').Length);
            double halfWidth = Math.Min(sourceImage.PixelWidth * 0.42, Math.Max(100, Text.Length * effectiveFontSize * 0.36));
            double halfHeight = Math.Max(34, effectiveFontSize * lineCount * 0.8);
            var center = new Point(
                sourceImage.PixelWidth / 2.0 + HorizontalOffset,
                sourceImage.PixelHeight / 2.0 + VerticalOffset);
            return new Rect(
                center.X - halfWidth,
                center.Y - halfHeight,
                halfWidth * 2,
                halfHeight * 2).Contains(point);
        }

        private static double GetAdaptiveScale(BitmapSource sourceImage)
        {
            double shortEdge = Math.Max(1, Math.Min(sourceImage.PixelWidth, sourceImage.PixelHeight));
            double scale = shortEdge / ReferenceShortEdge;
            return Math.Max(MinimumAdaptiveScale, Math.Min(MaximumAdaptiveScale, scale));
        }

        private Rect GetImageLogoBounds(BitmapSource sourceImage, BitmapSource logo)
        {
            double imageShortEdge = Math.Max(1, Math.Min(sourceImage.PixelWidth, sourceImage.PixelHeight));
            double logoWidth = Math.Max(12, imageShortEdge * Math.Max(1, Math.Min(80, LogoScalePercent)) / 100.0);
            double logoHeight = logoWidth * logo.PixelHeight / Math.Max(1.0, logo.PixelWidth);
            var center = new Point(
                sourceImage.PixelWidth / 2.0 + HorizontalOffset,
                sourceImage.PixelHeight / 2.0 + VerticalOffset);
            return new Rect(
                center.X - logoWidth / 2.0,
                center.Y - logoHeight / 2.0,
                logoWidth,
                logoHeight);
        }

        private static BitmapSource LoadLogo(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                BitmapSource cached;
                if (LogoCache.TryGetValue(path, out cached)) return cached;

                BitmapSource bitmap;
                string ext = Path.GetExtension(path);
                if (string.Equals(ext, ".webp", StringComparison.OrdinalIgnoreCase))
                {
                    bitmap = WebpDecoder.Load(path);
                }
                else
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    image.UriSource = new Uri(path, UriKind.Absolute);
                    image.EndInit();
                    image.Freeze();
                    bitmap = image;
                }

                LogoCache[path] = bitmap;
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }
}
