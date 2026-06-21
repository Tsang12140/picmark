using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PicMark
{
    public sealed class PicMarkProject
    {
        public BitmapSource Image { get; set; }
        public string SourceName { get; set; }
        public List<Annotation> Annotations { get; set; } = new List<Annotation>();
        public WatermarkSettings Watermark { get; set; }
    }

    public static class ProjectStore
    {
        private const int FormatVersion = 1;

        public static void Save(
            string path,
            BitmapSource image,
            string sourceName,
            IEnumerable<Annotation> annotations,
            WatermarkSettings watermark)
        {
            if (image == null) throw new InvalidOperationException("Project image is required.");

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tempPath = path + ".tmp";
            if (File.Exists(tempPath)) File.Delete(tempPath);

            var document = new ProjectDocument
            {
                Version = FormatVersion,
                SourceName = string.IsNullOrWhiteSpace(sourceName) ? "image.png" : sourceName,
                SourceEntry = "source.png",
                SavedAt = DateTime.UtcNow.ToString("o"),
                Annotations = annotations.Select(ToDto).Where(a => a != null).ToList(),
                Watermark = ToDto(watermark)
            };

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create, Encoding.UTF8))
            {
                var jsonEntry = archive.CreateEntry("project.json", CompressionLevel.Optimal);
                using (var writer = new StreamWriter(jsonEntry.Open(), new UTF8Encoding(false)))
                    writer.Write(serializer.Serialize(document));

                var imageEntry = archive.CreateEntry(document.SourceEntry, CompressionLevel.NoCompression);
                using (var stream = imageEntry.Open())
                    SavePng(image, stream);
            }

            if (File.Exists(path)) File.Delete(path);
            File.Move(tempPath, path);
        }

        public static PicMarkProject Load(string path)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            using (var archive = ZipFile.OpenRead(path))
            {
                var jsonEntry = archive.GetEntry("project.json");
                if (jsonEntry == null) throw new InvalidDataException("Missing project.json.");

                ProjectDocument document;
                using (var reader = new StreamReader(jsonEntry.Open(), Encoding.UTF8))
                    document = serializer.Deserialize<ProjectDocument>(reader.ReadToEnd());
                if (document == null || document.Version > FormatVersion)
                    throw new InvalidDataException("Unsupported project format.");

                var sourceEntry = archive.GetEntry(document.SourceEntry ?? "source.png");
                if (sourceEntry == null) throw new InvalidDataException("Missing source image.");

                BitmapSource image;
                using (var memory = new MemoryStream())
                {
                    using (var source = sourceEntry.Open())
                        source.CopyTo(memory);
                    memory.Position = 0;
                    image = LoadBitmap(memory);
                }

                return new PicMarkProject
                {
                    Image = image,
                    SourceName = document.SourceName,
                    Annotations = document.Annotations.Select(FromDto).Where(a => a != null).ToList(),
                    Watermark = FromDto(document.Watermark)
                };
            }
        }

        private static void SavePng(BitmapSource source, Stream stream)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var memory = new MemoryStream())
            {
                encoder.Save(memory);
                memory.Position = 0;
                memory.CopyTo(stream);
            }
        }

        private static BitmapSource LoadBitmap(Stream stream)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private static AnnotationDto ToDto(Annotation annotation)
        {
            if (annotation == null) return null;
            var dto = new AnnotationDto
            {
                Color = ColorToString(annotation.StrokeColor),
                Thickness = annotation.Thickness
            };

            if (annotation is OrganicRectAnnotation organicRect)
            {
                dto.Type = "OrganicRect";
                dto.Bounds = ToDto(organicRect.Bounds);
                dto.Points = organicRect.Points.Select(ToDto).ToList();
            }
            else if (annotation is RectAnnotation rect)
            {
                dto.Type = "Rect";
                dto.Bounds = ToDto(rect.Bounds);
            }
            else if (annotation is OrganicPolygonAnnotation polygon)
            {
                dto.Type = "OrganicPolygon";
                dto.Points = polygon.Vertices.Select(ToDto).ToList();
            }
            else if (annotation is OpenEllipseAnnotation openEllipse)
            {
                dto.Type = "OpenEllipse";
                dto.Bounds = ToDto(openEllipse.Bounds);
                dto.StartAngle = openEllipse.StartAngle;
                dto.SweepAngle = openEllipse.SweepAngle;
                dto.Points = openEllipse.Points.Select(ToDto).ToList();
            }
            else if (annotation is EllipseAnnotation ellipse)
            {
                dto.Type = "Ellipse";
                dto.Bounds = ToDto(ellipse.Bounds);
            }
            else if (annotation is ArrowAnnotation arrow)
            {
                dto.Type = "Arrow";
                dto.Start = ToDto(arrow.Start);
                dto.End = ToDto(arrow.End);
                dto.ArrowStyle = arrow.Style.ToString();
            }
            else if (annotation is FreehandAnnotation freehand)
            {
                dto.Type = "Freehand";
                dto.Points = freehand.Points.Select(ToDto).ToList();
            }
            else if (annotation is MosaicAnnotation mosaic)
            {
                dto.Type = "Mosaic";
                dto.Bounds = ToDto(mosaic.Bounds);
                dto.BlockSize = mosaic.BlockSize;
                dto.MosaicMode = mosaic.Mode.ToString();
            }
            else if (annotation is TextAnnotation text)
            {
                dto.Type = "Text";
                dto.Start = ToDto(text.Location);
                dto.Text = text.Text;
                dto.FontSize = text.FontSize;
            }
            return dto.Type == null ? null : dto;
        }

        private static Annotation FromDto(AnnotationDto dto)
        {
            if (dto == null) return null;
            Color color = ParseColor(dto.Color);
            double thickness = dto.Thickness <= 0 ? 6 : dto.Thickness;

            switch (dto.Type)
            {
                case "OrganicRect":
                    var organicRect = new OrganicRectAnnotation
                    {
                        Bounds = FromDto(dto.Bounds),
                        StrokeColor = color,
                        Thickness = thickness
                    };
                    AddPoints(organicRect.Points, dto.Points);
                    return organicRect;
                case "Rect":
                    return new RectAnnotation { Bounds = FromDto(dto.Bounds), StrokeColor = color, Thickness = thickness };
                case "OrganicPolygon":
                    var polygon = new OrganicPolygonAnnotation { StrokeColor = color, Thickness = thickness };
                    AddPoints(polygon.Vertices, dto.Points);
                    return polygon;
                case "OpenEllipse":
                    var openEllipse = new OpenEllipseAnnotation
                    {
                        Bounds = FromDto(dto.Bounds),
                        StartAngle = dto.StartAngle,
                        SweepAngle = dto.SweepAngle,
                        StrokeColor = color,
                        Thickness = thickness
                    };
                    AddPoints(openEllipse.Points, dto.Points);
                    return openEllipse;
                case "Ellipse":
                    return new EllipseAnnotation { Bounds = FromDto(dto.Bounds), StrokeColor = color, Thickness = thickness };
                case "Arrow":
                    return new ArrowAnnotation
                    {
                        Start = FromDto(dto.Start),
                        End = FromDto(dto.End),
                        StrokeColor = color,
                        Thickness = thickness,
                        Style = Enum.TryParse(dto.ArrowStyle, out ArrowStyle style) ? style : ArrowStyle.Filled
                    };
                case "Freehand":
                    var freehand = new FreehandAnnotation { StrokeColor = color, Thickness = thickness };
                    AddPoints(freehand.Points, dto.Points);
                    return freehand;
                case "Mosaic":
                    return new MosaicAnnotation
                    {
                        Bounds = FromDto(dto.Bounds),
                        BlockSize = dto.BlockSize <= 0 ? 18 : dto.BlockSize,
                        Mode = Enum.TryParse(dto.MosaicMode, out MosaicMode mode) ? mode : MosaicMode.Pixelate,
                        StrokeColor = color,
                        Thickness = thickness
                    };
                case "Text":
                    return new TextAnnotation
                    {
                        Location = FromDto(dto.Start),
                        Text = dto.Text ?? string.Empty,
                        FontSize = dto.FontSize <= 0 ? 36 : dto.FontSize,
                        StrokeColor = color,
                        Thickness = thickness
                    };
                default:
                    return null;
            }
        }

        private static void AddPoints(ICollection<Point> target, IEnumerable<PointDto> points)
        {
            if (points == null) return;
            foreach (var point in points)
                target.Add(FromDto(point));
        }

        private static WatermarkDto ToDto(WatermarkSettings watermark)
        {
            if (watermark == null) return null;
            return new WatermarkDto
            {
                Enabled = watermark.Enabled,
                Text = watermark.Text,
                Style = watermark.Style.ToString(),
                Layout = watermark.Layout.ToString(),
                Opacity = watermark.Opacity,
                FontSize = watermark.FontSize,
                Spacing = watermark.Spacing,
                HorizontalOffset = watermark.HorizontalOffset,
                VerticalOffset = watermark.VerticalOffset,
                Angle = watermark.Angle,
                Bold = watermark.Bold,
                FontFamilyName = watermark.FontFamilyName,
                Color = ColorToString(watermark.Color),
                LogoPath = watermark.LogoPath,
                LogoScalePercent = watermark.LogoScalePercent,
                LogoFlipHorizontal = watermark.LogoFlipHorizontal,
                LogoFlipVertical = watermark.LogoFlipVertical
            };
        }

        private static WatermarkSettings FromDto(WatermarkDto dto)
        {
            if (dto == null) return null;
            return new WatermarkSettings
            {
                Enabled = dto.Enabled,
                Text = dto.Text ?? string.Empty,
                Style = Enum.TryParse(dto.Style, out WatermarkStyle style) ? style : WatermarkStyle.DiamondGrid,
                Layout = Enum.TryParse(dto.Layout, out WatermarkLayout layout) ? layout : WatermarkLayout.Tiled,
                Opacity = dto.Opacity <= 0 ? 0.2 : dto.Opacity,
                FontSize = dto.FontSize <= 0 ? 28 : dto.FontSize,
                Spacing = dto.Spacing <= 0 ? 204 : dto.Spacing,
                HorizontalOffset = dto.HorizontalOffset,
                VerticalOffset = dto.VerticalOffset,
                Angle = dto.Angle,
                Bold = dto.Bold,
                FontFamilyName = string.IsNullOrWhiteSpace(dto.FontFamilyName) ? "Microsoft YaHei UI" : dto.FontFamilyName,
                Color = ParseColor(dto.Color),
                LogoPath = dto.LogoPath ?? string.Empty,
                LogoScalePercent = dto.LogoScalePercent <= 0 ? 18 : dto.LogoScalePercent,
                LogoFlipHorizontal = dto.LogoFlipHorizontal,
                LogoFlipVertical = dto.LogoFlipVertical
            };
        }

        private static RectDto ToDto(Rect rect) =>
            new RectDto { X = rect.X, Y = rect.Y, Width = rect.Width, Height = rect.Height };

        private static Rect FromDto(RectDto dto) =>
            dto == null ? Rect.Empty : new Rect(dto.X, dto.Y, dto.Width, dto.Height);

        private static PointDto ToDto(Point point) => new PointDto { X = point.X, Y = point.Y };

        private static Point FromDto(PointDto dto) => dto == null ? new Point() : new Point(dto.X, dto.Y);

        private static string ColorToString(Color color) =>
            $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

        private static Color ParseColor(string value)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return (Color)ColorConverter.ConvertFromString(value);
            }
            catch
            {
            }
            return Colors.Red;
        }

        private sealed class ProjectDocument
        {
            public int Version { get; set; }
            public string SavedAt { get; set; }
            public string SourceName { get; set; }
            public string SourceEntry { get; set; }
            public List<AnnotationDto> Annotations { get; set; } = new List<AnnotationDto>();
            public WatermarkDto Watermark { get; set; }
        }

        private sealed class AnnotationDto
        {
            public string Type { get; set; }
            public string Color { get; set; }
            public double Thickness { get; set; }
            public RectDto Bounds { get; set; }
            public PointDto Start { get; set; }
            public PointDto End { get; set; }
            public List<PointDto> Points { get; set; }
            public string ArrowStyle { get; set; }
            public int BlockSize { get; set; }
            public string MosaicMode { get; set; }
            public string Text { get; set; }
            public double FontSize { get; set; }
            public double StartAngle { get; set; }
            public double SweepAngle { get; set; }
        }

        private sealed class WatermarkDto
        {
            public bool Enabled { get; set; }
            public string Text { get; set; }
            public string Style { get; set; }
            public string Layout { get; set; }
            public double Opacity { get; set; }
            public double FontSize { get; set; }
            public double Spacing { get; set; }
            public double HorizontalOffset { get; set; }
            public double VerticalOffset { get; set; }
            public double Angle { get; set; }
            public bool Bold { get; set; }
            public string FontFamilyName { get; set; }
            public string Color { get; set; }
            public string LogoPath { get; set; }
            public double LogoScalePercent { get; set; }
            public bool LogoFlipHorizontal { get; set; }
            public bool LogoFlipVertical { get; set; }
        }

        private sealed class RectDto
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
        }

        private sealed class PointDto
        {
            public double X { get; set; }
            public double Y { get; set; }
        }
    }
}
