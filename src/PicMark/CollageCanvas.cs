using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PicMark
{
    public enum CollageTemplateKind
    {
        ThreeHorizontal,
        ThreeVertical,
        ThreeTopOneBottomTwo,
        ThreeLeftOneRightTwo,
        FourGrid,
        FourHorizontal,
        FourVertical,
        ComicLeftLarge,
        ComicTopLarge,
        SeamlessVertical,
        StackVertical,
        StackHorizontal
    }

    public sealed class CollageItem
    {
        public string Path { get; set; }
        public BitmapSource Image { get; set; }
        public double Zoom { get; set; } = 1.0;
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }

        public string DisplayName => Image == null ? "待添加图片" : System.IO.Path.GetFileName(Path);
        public double Aspect => Image == null || Image.PixelHeight <= 0 ? 1.0 : (double)Image.PixelWidth / Image.PixelHeight;

        public CollageItem WithImage(BitmapSource image)
        {
            return new CollageItem
            {
                Path = Path,
                Image = image,
                Zoom = Zoom,
                OffsetX = OffsetX,
                OffsetY = OffsetY
            };
        }
    }

    internal sealed class CollageDivider
    {
        public string Key { get; set; }
        public bool IsVertical { get; set; }
        public Rect HitArea { get; set; }
        public double Minimum { get; set; }
        public double Maximum { get; set; }
    }

    internal sealed class CollageLayoutResult
    {
        public List<Rect> Cells { get; } = new List<Rect>();
        public List<CollageDivider> Dividers { get; } = new List<CollageDivider>();
    }

    public class CollageSlotEventArgs : EventArgs
    {
        public CollageSlotEventArgs(int slotIndex)
        {
            SlotIndex = slotIndex;
        }

        public int SlotIndex { get; }
    }

    public sealed class CollageSlotDropEventArgs : CollageSlotEventArgs
    {
        public CollageSlotDropEventArgs(int slotIndex, string[] paths)
            : base(slotIndex)
        {
            Paths = paths ?? Array.Empty<string>();
        }

        public string[] Paths { get; }
    }

    public sealed class CollageCanvas : FrameworkElement
    {
        private readonly Dictionary<string, double> _ratios = new Dictionary<string, double>();
        private IList<CollageItem> _items = new List<CollageItem>();
        private CollageTemplateKind _template = CollageTemplateKind.FourGrid;
        private double _gap = 12;
        private Brush _canvasBackground = Brushes.White;
        private int _selectedIndex = -1;
        private CollageDivider _activeDivider;
        private Point _lastMouse;
        private bool _draggingImage;
        private int _dropTargetIndex = -1;

        public event EventHandler SelectedIndexChanged;
        public event EventHandler<CollageSlotEventArgs> EmptySlotClicked;
        public event EventHandler<CollageSlotDropEventArgs> EmptySlotDropped;

        public IList<CollageItem> Items
        {
            get => _items;
            set
            {
                _items = value ?? new List<CollageItem>();
                if (_selectedIndex >= _items.Count) _selectedIndex = _items.Count - 1;
                InvalidateVisual();
            }
        }

        public CollageTemplateKind Template
        {
            get => _template;
            set
            {
                _template = value;
                _ratios.Clear();
                InvalidateVisual();
            }
        }

        public double Gap
        {
            get => _gap;
            set
            {
                _gap = Math.Max(0, value);
                InvalidateVisual();
            }
        }

        public Brush CanvasBackground
        {
            get => _canvasBackground;
            set
            {
                _canvasBackground = value ?? Brushes.White;
                InvalidateVisual();
            }
        }

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                int next = value < -1 ? -1 : Math.Min(value, _items.Count - 1);
                if (_selectedIndex == next) return;
                _selectedIndex = next;
                InvalidateVisual();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public static int RequiredImageCount(CollageTemplateKind template)
        {
            switch (template)
            {
                case CollageTemplateKind.ThreeHorizontal:
                case CollageTemplateKind.ThreeVertical:
                case CollageTemplateKind.ThreeTopOneBottomTwo:
                case CollageTemplateKind.ThreeLeftOneRightTwo:
                case CollageTemplateKind.ComicLeftLarge:
                    return 3;
                case CollageTemplateKind.FourGrid:
                case CollageTemplateKind.FourHorizontal:
                case CollageTemplateKind.FourVertical:
                case CollageTemplateKind.ComicTopLarge:
                    return 4;
                case CollageTemplateKind.StackVertical:
                case CollageTemplateKind.StackHorizontal:
                case CollageTemplateKind.SeamlessVertical:
                    return 2;
                default:
                    return 2;
            }
        }

        public static bool IsFlowTemplate(CollageTemplateKind template)
        {
            return template == CollageTemplateKind.StackVertical
                || template == CollageTemplateKind.StackHorizontal
                || template == CollageTemplateKind.SeamlessVertical;
        }

        public static double NaturalAspect(CollageTemplateKind template, IList<CollageItem> items)
        {
            int count = Math.Max(1, items == null ? 0 : items.Count);
            switch (template)
            {
                case CollageTemplateKind.ThreeHorizontal: return 3.0;
                case CollageTemplateKind.ThreeVertical: return 1.0 / 3.0;
                case CollageTemplateKind.FourHorizontal: return 4.0;
                case CollageTemplateKind.FourVertical: return 0.25;
                case CollageTemplateKind.FourGrid: return 1.0;
                case CollageTemplateKind.StackHorizontal:
                    return items == null || items.Count == 0 ? 2.0 : Math.Max(0.2, items.Sum(item => item.Aspect));
                case CollageTemplateKind.StackVertical:
                case CollageTemplateKind.SeamlessVertical:
                    if (items == null || items.Count == 0) return 0.6;
                    double heightUnits = items.Sum(item => 1.0 / Math.Max(0.05, item.Aspect));
                    return 1.0 / Math.Max(0.05, heightUnits);
                default:
                    return 4.0 / 3.0;
            }
        }

        public void ResetSelectedImage()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _items.Count) return;
            _items[_selectedIndex].Zoom = 1;
            _items[_selectedIndex].OffsetX = 0;
            _items[_selectedIndex].OffsetY = 0;
            InvalidateVisual();
        }

        public RenderTargetBitmap RenderBitmap(int pixelWidth, int pixelHeight, IList<CollageItem> renderItems)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                DrawScene(dc, new Size(pixelWidth, pixelHeight), renderItems ?? _items, false);
            }
            var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            DrawScene(dc, RenderSize, _items, true);
        }

        private void DrawScene(DrawingContext dc, Size size, IList<CollageItem> items, bool interactive)
        {
            if (size.Width <= 0 || size.Height <= 0) return;
            dc.DrawRectangle(_canvasBackground, null, new Rect(new Point(0, 0), size));

            double scaledGap = _template == CollageTemplateKind.SeamlessVertical ? 0 : size.Width * (_gap / 1600.0);
            var layout = BuildLayout(size, items, scaledGap);
            for (int i = 0; i < layout.Cells.Count; i++)
            {
                Rect cell = layout.Cells[i];
                if (cell.Width <= 0 || cell.Height <= 0) continue;
                if (i < items.Count && items[i]?.Image != null)
                    DrawImageCell(dc, items[i], cell, !IsFlowTemplate(_template));
                else
                    DrawEmptyCell(dc, cell, i + 1);

                if (interactive && i == _selectedIndex)
                    dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0x2F, 0xA8, 0xFF)), 3), cell);
                if (interactive && i == _dropTargetIndex)
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(35, 47, 168, 255)), new Pen(new SolidColorBrush(Color.FromRgb(0x2F, 0xA8, 0xFF)), 3), cell);
            }

            if (!interactive) return;
            var dividerPen = new Pen(new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)), 1);
            foreach (var divider in layout.Dividers)
            {
                if (divider.IsVertical)
                {
                    double x = divider.HitArea.X + divider.HitArea.Width / 2;
                    dc.DrawLine(dividerPen, new Point(x, divider.HitArea.Top), new Point(x, divider.HitArea.Bottom));
                }
                else
                {
                    double y = divider.HitArea.Y + divider.HitArea.Height / 2;
                    dc.DrawLine(dividerPen, new Point(divider.HitArea.Left, y), new Point(divider.HitArea.Right, y));
                }
            }
        }

        private static void DrawImageCell(DrawingContext dc, CollageItem item, Rect cell, bool cover)
        {
            BitmapSource image = item.Image;
            double scale = cover
                ? Math.Max(cell.Width / image.PixelWidth, cell.Height / image.PixelHeight)
                : Math.Min(cell.Width / image.PixelWidth, cell.Height / image.PixelHeight);
            scale *= Math.Max(1, item.Zoom);
            double width = image.PixelWidth * scale;
            double height = image.PixelHeight * scale;
            double x = cell.X + (cell.Width - width) / 2 + item.OffsetX * cell.Width;
            double y = cell.Y + (cell.Height - height) / 2 + item.OffsetY * cell.Height;
            dc.PushClip(new RectangleGeometry(cell));
            dc.DrawImage(image, new Rect(x, y, width, height));
            dc.Pop();
        }

        private static void DrawEmptyCell(DrawingContext dc, Rect cell, int number)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x35, 0x38, 0x3D)), null, cell);
            var text = new FormattedText(
                "+  添加图片 " + number,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(UiFonts.Family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                Math.Max(12, Math.Min(20, cell.Width / 12)),
                new SolidColorBrush(Color.FromRgb(0xC5, 0xCA, 0xD1)),
                1.0);
            dc.DrawText(text, new Point(cell.X + (cell.Width - text.Width) / 2, cell.Y + (cell.Height - text.Height) / 2));
        }

        private CollageLayoutResult BuildLayout(Size size, IList<CollageItem> items, double gap)
        {
            var result = new CollageLayoutResult();
            double w = size.Width;
            double h = size.Height;
            switch (_template)
            {
                case CollageTemplateKind.ThreeHorizontal:
                    AddLinear(result, w, h, 3, true, gap);
                    break;
                case CollageTemplateKind.ThreeVertical:
                    AddLinear(result, w, h, 3, false, gap);
                    break;
                case CollageTemplateKind.FourHorizontal:
                    AddLinear(result, w, h, 4, true, gap);
                    break;
                case CollageTemplateKind.FourVertical:
                    AddLinear(result, w, h, 4, false, gap);
                    break;
                case CollageTemplateKind.FourGrid:
                    AddGrid(result, w, h, gap);
                    break;
                case CollageTemplateKind.ThreeTopOneBottomTwo:
                    AddTopOneBottomTwo(result, w, h, gap);
                    break;
                case CollageTemplateKind.ThreeLeftOneRightTwo:
                case CollageTemplateKind.ComicLeftLarge:
                    AddLeftOneRightTwo(result, w, h, gap);
                    break;
                case CollageTemplateKind.ComicTopLarge:
                    AddTopOneBottomThree(result, w, h, gap);
                    break;
                case CollageTemplateKind.StackHorizontal:
                    AddFlow(result, w, h, items, true, gap);
                    break;
                case CollageTemplateKind.StackVertical:
                case CollageTemplateKind.SeamlessVertical:
                    AddFlow(result, w, h, items, false, gap);
                    break;
                default:
                    AddGrid(result, w, h, gap);
                    break;
            }
            return result;
        }

        private void AddLinear(CollageLayoutResult result, double w, double h, int count, bool horizontal, double gap)
        {
            var boundaries = new double[count + 1];
            boundaries[0] = 0;
            boundaries[count] = 1;
            for (int i = 1; i < count; i++)
            {
                double previous = boundaries[i - 1];
                double fallback = (double)i / count;
                double nextFallback = i == count - 1 ? 1 : (double)(i + 1) / count;
                boundaries[i] = Clamp(GetRatio((horizontal ? "x" : "y") + i, fallback), previous + 0.1, nextFallback + 0.15);
            }
            for (int i = 1; i < count; i++)
                boundaries[i] = Math.Min(boundaries[i], boundaries[i + 1] - 0.1);

            for (int i = 0; i < count; i++)
            {
                if (horizontal)
                {
                    double left = boundaries[i] * w + (i == 0 ? 0 : gap / 2);
                    double right = boundaries[i + 1] * w - (i == count - 1 ? 0 : gap / 2);
                    result.Cells.Add(new Rect(left, 0, Math.Max(0, right - left), h));
                }
                else
                {
                    double top = boundaries[i] * h + (i == 0 ? 0 : gap / 2);
                    double bottom = boundaries[i + 1] * h - (i == count - 1 ? 0 : gap / 2);
                    result.Cells.Add(new Rect(0, top, w, Math.Max(0, bottom - top)));
                }
            }
            for (int i = 1; i < count; i++)
            {
                double min = boundaries[i - 1] + 0.1;
                double max = boundaries[i + 1] - 0.1;
                if (horizontal) AddDivider(result, "x" + i, true, boundaries[i] * w, 0, h, min, max);
                else AddDivider(result, "y" + i, false, boundaries[i] * h, 0, w, min, max);
            }
        }

        private void AddGrid(CollageLayoutResult result, double w, double h, double gap)
        {
            double x = Clamp(GetRatio("x", 0.5), 0.2, 0.8);
            double y = Clamp(GetRatio("y", 0.5), 0.2, 0.8);
            double px = x * w;
            double py = y * h;
            result.Cells.Add(new Rect(0, 0, Math.Max(0, px - gap / 2), Math.Max(0, py - gap / 2)));
            result.Cells.Add(new Rect(px + gap / 2, 0, Math.Max(0, w - px - gap / 2), Math.Max(0, py - gap / 2)));
            result.Cells.Add(new Rect(0, py + gap / 2, Math.Max(0, px - gap / 2), Math.Max(0, h - py - gap / 2)));
            result.Cells.Add(new Rect(px + gap / 2, py + gap / 2, Math.Max(0, w - px - gap / 2), Math.Max(0, h - py - gap / 2)));
            AddDivider(result, "x", true, px, 0, h, 0.2, 0.8);
            AddDivider(result, "y", false, py, 0, w, 0.2, 0.8);
        }

        private void AddTopOneBottomTwo(CollageLayoutResult result, double w, double h, double gap)
        {
            double y = Clamp(GetRatio("y", 0.55), 0.25, 0.75);
            double x = Clamp(GetRatio("x", 0.5), 0.2, 0.8);
            double px = x * w;
            double py = y * h;
            result.Cells.Add(new Rect(0, 0, w, Math.Max(0, py - gap / 2)));
            result.Cells.Add(new Rect(0, py + gap / 2, Math.Max(0, px - gap / 2), Math.Max(0, h - py - gap / 2)));
            result.Cells.Add(new Rect(px + gap / 2, py + gap / 2, Math.Max(0, w - px - gap / 2), Math.Max(0, h - py - gap / 2)));
            AddDivider(result, "y", false, py, 0, w, 0.25, 0.75);
            AddDivider(result, "x", true, px, py, h, 0.2, 0.8);
        }

        private void AddLeftOneRightTwo(CollageLayoutResult result, double w, double h, double gap)
        {
            double x = Clamp(GetRatio("x", 0.58), 0.25, 0.75);
            double y = Clamp(GetRatio("y", 0.5), 0.2, 0.8);
            double px = x * w;
            double py = y * h;
            result.Cells.Add(new Rect(0, 0, Math.Max(0, px - gap / 2), h));
            result.Cells.Add(new Rect(px + gap / 2, 0, Math.Max(0, w - px - gap / 2), Math.Max(0, py - gap / 2)));
            result.Cells.Add(new Rect(px + gap / 2, py + gap / 2, Math.Max(0, w - px - gap / 2), Math.Max(0, h - py - gap / 2)));
            AddDivider(result, "x", true, px, 0, h, 0.25, 0.75);
            AddDivider(result, "y", false, py, px, w, 0.2, 0.8);
        }

        private void AddTopOneBottomThree(CollageLayoutResult result, double w, double h, double gap)
        {
            double y = Clamp(GetRatio("y", 0.62), 0.3, 0.8);
            double x1 = Clamp(GetRatio("x1", 1.0 / 3), 0.15, 0.55);
            double x2 = Clamp(GetRatio("x2", 2.0 / 3), x1 + 0.15, 0.85);
            double py = y * h;
            result.Cells.Add(new Rect(0, 0, w, Math.Max(0, py - gap / 2)));
            double[] xs = { 0, x1, x2, 1 };
            for (int i = 0; i < 3; i++)
            {
                double left = xs[i] * w + (i == 0 ? 0 : gap / 2);
                double right = xs[i + 1] * w - (i == 2 ? 0 : gap / 2);
                result.Cells.Add(new Rect(left, py + gap / 2, Math.Max(0, right - left), Math.Max(0, h - py - gap / 2)));
            }
            AddDivider(result, "y", false, py, 0, w, 0.3, 0.8);
            AddDivider(result, "x1", true, x1 * w, py, h, 0.15, x2 - 0.15);
            AddDivider(result, "x2", true, x2 * w, py, h, x1 + 0.15, 0.85);
        }

        private static void AddFlow(CollageLayoutResult result, double w, double h, IList<CollageItem> items, bool horizontal, double gap)
        {
            int count = Math.Max(1, items == null ? 0 : items.Count);
            var weights = new double[count];
            double total = 0;
            for (int i = 0; i < count; i++)
            {
                double aspect = items != null && i < items.Count ? items[i].Aspect : 1;
                weights[i] = horizontal ? Math.Max(0.05, aspect) : 1.0 / Math.Max(0.05, aspect);
                total += weights[i];
            }
            double cursor = 0;
            for (int i = 0; i < count; i++)
            {
                double fraction = weights[i] / Math.Max(0.01, total);
                if (horizontal)
                {
                    double width = i == count - 1 ? w - cursor : w * fraction;
                    double left = cursor + (i == 0 ? 0 : gap / 2);
                    double right = cursor + width - (i == count - 1 ? 0 : gap / 2);
                    result.Cells.Add(new Rect(left, 0, Math.Max(0, right - left), h));
                    cursor += width;
                }
                else
                {
                    double height = i == count - 1 ? h - cursor : h * fraction;
                    double top = cursor + (i == 0 ? 0 : gap / 2);
                    double bottom = cursor + height - (i == count - 1 ? 0 : gap / 2);
                    result.Cells.Add(new Rect(0, top, w, Math.Max(0, bottom - top)));
                    cursor += height;
                }
            }
        }

        private static void AddDivider(CollageLayoutResult result, string key, bool vertical, double position, double rangeStart, double rangeEnd, double min, double max)
        {
            const double hit = 12;
            result.Dividers.Add(new CollageDivider
            {
                Key = key,
                IsVertical = vertical,
                HitArea = vertical
                    ? new Rect(position - hit / 2, rangeStart, hit, Math.Max(0, rangeEnd - rangeStart))
                    : new Rect(rangeStart, position - hit / 2, Math.Max(0, rangeEnd - rangeStart), hit),
                Minimum = min,
                Maximum = max
            });
        }

        private double GetRatio(string key, double fallback)
        {
            return _ratios.TryGetValue(key, out double value) ? value : fallback;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Focus();
            Point point = e.GetPosition(this);
            var layout = BuildLayout(RenderSize, _items, RenderSize.Width * (_gap / 1600.0));
            _activeDivider = layout.Dividers.FirstOrDefault(divider => divider.HitArea.Contains(point));
            if (_activeDivider != null)
            {
                CaptureMouse();
                e.Handled = true;
                return;
            }

            int index = layout.Cells.FindIndex(cell => cell.Contains(point));
            if (index >= 0 && index < _items.Count)
            {
                if (IsEmptySlot(index))
                {
                    EmptySlotClicked?.Invoke(this, new CollageSlotEventArgs(index));
                }
                else
                {
                    SelectedIndex = index;
                    _lastMouse = point;
                    _draggingImage = true;
                    CaptureMouse();
                    if (e.ClickCount == 2) ResetSelectedImage();
                }
                e.Handled = true;
            }
            else if (index >= 0)
            {
                EmptySlotClicked?.Invoke(this, new CollageSlotEventArgs(index));
                e.Handled = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point point = e.GetPosition(this);
            if (_activeDivider != null && e.LeftButton == MouseButtonState.Pressed)
            {
                double normalized = _activeDivider.IsVertical
                    ? point.X / Math.Max(1, ActualWidth)
                    : point.Y / Math.Max(1, ActualHeight);
                _ratios[_activeDivider.Key] = Clamp(normalized, _activeDivider.Minimum, _activeDivider.Maximum);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (_draggingImage && e.LeftButton == MouseButtonState.Pressed && _selectedIndex >= 0 && _selectedIndex < _items.Count)
            {
                Vector delta = point - _lastMouse;
                _lastMouse = point;
                var item = _items[_selectedIndex];
                item.OffsetX = Clamp(item.OffsetX + delta.X / Math.Max(1, ActualWidth), -1.5, 1.5);
                item.OffsetY = Clamp(item.OffsetY + delta.Y / Math.Max(1, ActualHeight), -1.5, 1.5);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            var hoverLayout = BuildLayout(RenderSize, _items, RenderSize.Width * (_gap / 1600.0));
            var hoverDivider = hoverLayout.Dividers.FirstOrDefault(divider => divider.HitArea.Contains(point));
            int hoverIndex = hoverLayout.Cells.FindIndex(cell => cell.Contains(point));
            Cursor = hoverDivider != null
                ? (hoverDivider.IsVertical ? Cursors.SizeWE : Cursors.SizeNS)
                : (hoverIndex >= 0 && IsEmptySlot(hoverIndex) ? Cursors.Hand : Cursors.Arrow);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            _activeDivider = null;
            _draggingImage = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_selectedIndex < 0 || _selectedIndex >= _items.Count) return;
            var item = _items[_selectedIndex];
            item.Zoom = Clamp(item.Zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), 1, 5);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            int target = GetEmptySlotAt(e.GetPosition(this));
            SetDropTarget(target);
            e.Effects = target >= 0 && e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        protected override void OnDragLeave(DragEventArgs e)
        {
            base.OnDragLeave(e);
            SetDropTarget(-1);
        }

        protected override void OnDrop(DragEventArgs e)
        {
            base.OnDrop(e);
            int target = GetEmptySlotAt(e.GetPosition(this));
            SetDropTarget(-1);
            if (target >= 0 && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                EmptySlotDropped?.Invoke(this, new CollageSlotDropEventArgs(target, paths));
                e.Handled = true;
            }
        }

        private int GetEmptySlotAt(Point point)
        {
            var layout = BuildLayout(RenderSize, _items, RenderSize.Width * (_gap / 1600.0));
            int index = layout.Cells.FindIndex(cell => cell.Contains(point));
            return index >= 0 && IsEmptySlot(index) ? index : -1;
        }

        private bool IsEmptySlot(int index)
        {
            return index < 0 || index >= _items.Count || _items[index] == null || _items[index].Image == null;
        }

        private void SetDropTarget(int index)
        {
            if (_dropTargetIndex == index) return;
            _dropTargetIndex = index;
            InvalidateVisual();
        }

        private static double Clamp(double value, double min, double max)
        {
            if (max < min) max = min;
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
