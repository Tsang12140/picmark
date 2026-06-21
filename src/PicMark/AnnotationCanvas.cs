using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PicMark
{
    public enum ImageTransformOperation
    {
        RotateLeft90,
        RotateRight90,
        Rotate180,
        FlipHorizontal,
        FlipVertical
    }

    public class AnnotationCanvas : Canvas
    {
        private const string DefaultFontFamily = "Alibaba PuHuiTi 3.0, Alibaba PuHuiTi, Microsoft YaHei UI, Microsoft YaHei, SimHei";

        private BitmapSource _image;
        private double _scale = 1.0;
        private Annotation _selected;
        private Annotation _drawing;
        private Point _dragStartImagePoint;
        private bool _isMovingSelection;
        private bool _selectionMoved;
        private TextAnnotation _editingText;
        private bool _editingTextWasNew;
        private string _editingOriginalText;
        private double _editingOriginalFontSize;
        private TextBox _textEditor;
        private readonly UndoManager _undo = new UndoManager();
        private readonly DispatcherTimer _beautifyTimer;
        private DateTime _lastFreehandStrokeAt;
        private WatermarkSettings _watermark;
        private bool _isMovingSingleWatermark;
        private Point _watermarkDragStartImagePoint;
        private enum CropHandle { None, Move, N, S, E, W, NE, NW, SE, SW }
        private Rect? _cropRect;
        private double? _cropAspectRatio;
        private CropHandle _cropDragHandle = CropHandle.None;
        private Point _cropDragStartImagePoint;
        private Rect _cropDragStartRect;
        private const double CropHandleScreenSize = 9;

        // Win7 兼容：缓存渲染画笔，避免高频 OnRender 中每帧分配
        private SolidColorBrush _cacheBackgroundBrush;
        private SolidColorBrush _cacheEditingBgBrush;
        private Pen _cacheEditingPen;

        public List<Annotation> Annotations { get; } = new List<Annotation>();
        public AnnotationTool CurrentTool { get; set; } = AnnotationTool.Select;
        public Color CurrentColor { get; set; } = Colors.Red;
        public double CurrentThickness { get; set; } = 9;
        public double CurrentFontSize { get; set; } = 36;
        public ArrowStyle CurrentArrowStyle { get; set; } = ArrowStyle.Filled;
        public MosaicMode CurrentMosaicMode { get; set; } = MosaicMode.Pixelate;
        public int CurrentMosaicStrength { get; set; } = 18;
        private bool _isEditingEnabled = true;

        public event EventHandler AnnotationsChanged;
        public event EventHandler SelectionChanged;
        public event EventHandler TextEditFinished;
        public event EventHandler ScaleChanged;
        public event EventHandler WatermarkChanged;

        public Annotation Selected => _selected;
        public bool IsTextSelected => _selected is TextAnnotation;
        public bool HasWatermark =>
            _watermark != null &&
            _watermark.Enabled &&
            (!string.IsNullOrWhiteSpace(_watermark.Text) ||
             (_watermark.Style == WatermarkStyle.ImageLogo && !string.IsNullOrWhiteSpace(_watermark.LogoPath)));

        public BitmapSource Image
        {
            get => _image;
            set
            {
                _image = value;
                UpdateDisplaySize();
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        public double Scale
        {
            get => _scale;
            set
            {
                double next = Math.Max(0.05, Math.Min(value, 8.0));
                if (Math.Abs(_scale - next) < 0.0001) return;
                _scale = next;
                UpdateDisplaySize();
                InvalidateMeasure();
                InvalidateVisual();
                ScaleChanged?.Invoke(this, EventArgs.Empty);
                PositionTextEditor();
            }
        }

        public bool HasSelection => _selected != null;
        public bool HasPendingCrop => CurrentTool == AnnotationTool.Crop && _cropRect.HasValue;
        public bool IsEditingEnabled
        {
            get => _isEditingEnabled;
            set
            {
                if (_isEditingEnabled == value) return;
                _isEditingEnabled = value;
                if (!value)
                    DisableEditingInteractions();
                InvalidateVisual();
            }
        }

        public AnnotationCanvas()
        {
            Focusable = true;
            ClipToBounds = true;
            Background = Brushes.Transparent;
            _beautifyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _beautifyTimer.Tick += BeautifyTimer_Tick;
            Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _beautifyTimer.Stop();
            _beautifyTimer.Tick -= BeautifyTimer_Tick;
            RemoveTextEditor();
            Unloaded -= OnUnloaded;
        }

        private void DisableEditingInteractions()
        {
            if (_editingText != null)
                CommitTextEdit();
            _beautifyTimer.Stop();
            _drawing = null;
            _isMovingSelection = false;
            _selectionMoved = false;
            _isMovingSingleWatermark = false;
            _cropDragHandle = CropHandle.None;
            _cropRect = null;
            Cursor = null;
            if (IsMouseCaptured)
                ReleaseMouseCapture();
            SetSelected(null);
        }

        private void UpdateDisplaySize()
        {
            if (Image == null)
            {
                Width = double.NaN;
                Height = double.NaN;
                return;
            }

            Width = Image.PixelWidth * Scale;
            Height = Image.PixelHeight * Scale;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            base.MeasureOverride(availableSize);
            if (Image == null) return new Size(0, 0);
            return new Size(Image.PixelWidth * Scale, Image.PixelHeight * Scale);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            PositionTextEditor();
            base.ArrangeOverride(finalSize);
            return finalSize;
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (Image == null)
            {
                if (_cacheBackgroundBrush == null)
                    _cacheBackgroundBrush = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77));
                dc.DrawRectangle(_cacheBackgroundBrush, null, new Rect(RenderSize));
                return;
            }

            dc.PushTransform(new ScaleTransform(Scale, Scale));
            try
            {
                dc.DrawImage(Image, new Rect(0, 0, Image.PixelWidth, Image.PixelHeight));
                // 快照避免渲染过程中 Annotations 被事件处理器修改
                var snapshot = Annotations.ToArray();
                foreach (var a in snapshot)
                {
                    if (ReferenceEquals(a, _editingText) && _textEditor == null)
                        DrawEditingText(dc);
                    else if (ReferenceEquals(a, _editingText))
                        continue;
                    else
                        a.Draw(dc, _isEditingEnabled && a == _selected, Image);
                }
                if (_isEditingEnabled)
                    _drawing?.Draw(dc, false, Image);
                _watermark?.Draw(dc, Image);
                if (_isEditingEnabled && CurrentTool == AnnotationTool.Crop && _cropRect.HasValue)
                    DrawCropOverlay(dc, _cropRect.Value);
            }
            finally
            {
                dc.Pop();
            }
        }

        private void DrawCropOverlay(DrawingContext dc, Rect crop)
        {
            double imgW = Image.PixelWidth;
            double imgH = Image.PixelHeight;
            var mask = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
            mask.Freeze();
            dc.DrawRectangle(mask, null, new Rect(0, 0, imgW, crop.Top));
            dc.DrawRectangle(mask, null, new Rect(0, crop.Bottom, imgW, imgH - crop.Bottom));
            dc.DrawRectangle(mask, null, new Rect(0, crop.Top, crop.Left, crop.Height));
            dc.DrawRectangle(mask, null, new Rect(crop.Right, crop.Top, imgW - crop.Right, crop.Height));

            double inv = _scale > 0 ? 1.0 / _scale : 1.0;
            var borderPen = new Pen(Brushes.White, 1.5 * inv);
            borderPen.Freeze();
            dc.DrawRectangle(null, borderPen, crop);

            var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)), 1 * inv);
            gridPen.Freeze();
            for (int i = 1; i <= 2; i++)
            {
                double gx = crop.X + crop.Width * i / 3.0;
                dc.DrawLine(gridPen, new Point(gx, crop.Top), new Point(gx, crop.Bottom));
                double gy = crop.Y + crop.Height * i / 3.0;
                dc.DrawLine(gridPen, new Point(crop.Left, gy), new Point(crop.Right, gy));
            }

            double hs = CropHandleScreenSize * inv;
            foreach (var hp in GetCropHandlePoints(crop))
                dc.DrawRectangle(Brushes.White, borderPen, new Rect(hp.X - hs / 2, hp.Y - hs / 2, hs, hs));
        }

        private static IEnumerable<Point> GetCropHandlePoints(Rect r)
        {
            yield return new Point(r.Left, r.Top);
            yield return new Point(r.Right, r.Top);
            yield return new Point(r.Left, r.Bottom);
            yield return new Point(r.Right, r.Bottom);
            yield return new Point(r.Left + r.Width / 2, r.Top);
            yield return new Point(r.Left + r.Width / 2, r.Bottom);
            yield return new Point(r.Left, r.Top + r.Height / 2);
            yield return new Point(r.Right, r.Top + r.Height / 2);
        }

        private Point ToImagePoint(Point screenPoint)
        {
            if (_scale <= 0) return new Point(0, 0);
            return new Point(screenPoint.X / Scale, screenPoint.Y / Scale);
        }

        public bool CanDragWatermarkAt(Point screenPoint)
        {
            if (Image == null || !IsEditingEnabled || _watermark == null) return false;
            if (_watermark.Style != WatermarkStyle.ImageLogo && _watermark.Layout != WatermarkLayout.Single) return false;
            return _watermark.HitTestSingle(ToImagePoint(screenPoint), Image, Scale);
        }

        public bool TryBeginWatermarkDrag(Point screenPoint)
        {
            if (!CanDragWatermarkAt(screenPoint)) return false;
            _isMovingSingleWatermark = true;
            _watermarkDragStartImagePoint = ToImagePoint(screenPoint);
            CaptureMouse();
            Focus();
            return true;
        }

        private void MoveWatermarkDrag(Point screenPoint)
        {
            if (!_isMovingSingleWatermark || _watermark == null) return;
            Point p = ToImagePoint(screenPoint);
            Vector delta = p - _watermarkDragStartImagePoint;
            _watermark.HorizontalOffset += delta.X;
            _watermark.VerticalOffset += delta.Y;
            _watermarkDragStartImagePoint = p;
            InvalidateVisual();
            WatermarkChanged?.Invoke(this, EventArgs.Empty);
        }

        private void EndWatermarkDrag()
        {
            if (!_isMovingSingleWatermark) return;
            _isMovingSingleWatermark = false;
            if (IsMouseCaptured)
                ReleaseMouseCapture();
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
            WatermarkChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            if (_isMovingSingleWatermark && IsEditingEnabled)
            {
                MoveWatermarkDrag(e.GetPosition(this));
                e.Handled = true;
                return;
            }
            base.OnPreviewMouseMove(e);
        }

        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (_isMovingSingleWatermark && IsEditingEnabled)
            {
                EndWatermarkDrag();
                e.Handled = true;
                return;
            }
            base.OnPreviewMouseLeftButtonUp(e);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (Image == null) return;
            if (!IsEditingEnabled) return;
            Focus();
            var p = ToImagePoint(e.GetPosition(this));

            if (TryBeginWatermarkDrag(e.GetPosition(this)))
            {
                e.Handled = true;
                return;
            }

            switch (CurrentTool)
            {
                case AnnotationTool.Rectangle:
                    _dragStartImagePoint = p;
                    _drawing = new RectAnnotation { Bounds = new Rect(p, p), StrokeColor = CurrentColor, Thickness = CurrentThickness };
                    if (!CaptureMouse()) { _drawing = null; }
                    break;
                case AnnotationTool.Ellipse:
                    _dragStartImagePoint = p;
                    _drawing = new EllipseAnnotation { Bounds = new Rect(p, p), StrokeColor = CurrentColor, Thickness = CurrentThickness };
                    if (!CaptureMouse()) { _drawing = null; }
                    break;
                case AnnotationTool.Arrow:
                    _dragStartImagePoint = p;
                    _drawing = new ArrowAnnotation { Start = p, End = p, StrokeColor = CurrentColor, Thickness = CurrentThickness, Style = CurrentArrowStyle };
                    if (!CaptureMouse()) { _drawing = null; }
                    break;
                case AnnotationTool.Freehand:
                    _dragStartImagePoint = p;
                    _drawing = new FreehandAnnotation { StrokeColor = CurrentColor, Thickness = CurrentThickness };
                    ((FreehandAnnotation)_drawing).Points.Add(p);
                    StartBeautifyWatch();
                    if (!CaptureMouse()) { _drawing = null; _beautifyTimer.Stop(); }
                    break;
                case AnnotationTool.Mosaic:
                    _dragStartImagePoint = p;
                    _drawing = new MosaicAnnotation { Bounds = new Rect(p, p), Mode = CurrentMosaicMode, BlockSize = CurrentMosaicStrength };
                    if (!CaptureMouse()) { _drawing = null; }
                    break;
                case AnnotationTool.Text:
                    BeginTextAnnotation(p);
                    e.Handled = true;
                    break;
                case AnnotationTool.Crop:
                    if (_cropRect.HasValue)
                    {
                        var handle = HitTestCropHandle(p, _cropRect.Value);
                        if (handle != CropHandle.None)
                        {
                            _cropDragHandle = handle;
                            _cropDragStartImagePoint = p;
                            _cropDragStartRect = _cropRect.Value;
                            CaptureMouse();
                        }
                    }
                    e.Handled = true;
                    break;
                case AnnotationTool.Select:
                default:
                    var hit = HitTest(p);
                    if (e.ClickCount == 2 && hit is TextAnnotation textHit)
                    {
                        BeginEditTextAnnotation(textHit);
                        e.Handled = true;
                        break;
                    }
                    SetSelected(hit);
                    if (hit != null)
                    {
                        _isMovingSelection = true;
                        _selectionMoved = false;
                        _dragStartImagePoint = p;
                        if (!CaptureMouse()) { _isMovingSelection = false; }
                    }
                    break;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (Image == null) return;
            if (!IsEditingEnabled) return;
            if (_isMovingSingleWatermark)
            {
                MoveWatermarkDrag(e.GetPosition(this));
                e.Handled = true;
                return;
            }
            var p = ToImagePoint(e.GetPosition(this));

            if (_drawing is RectAnnotation r)
            {
                r.Bounds = new Rect(_dragStartImagePoint, p);
                InvalidateVisual();
            }
            else if (_drawing is EllipseAnnotation el)
            {
                el.Bounds = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                    ? BuildSquareRect(_dragStartImagePoint, p)
                    : new Rect(_dragStartImagePoint, p);
                InvalidateVisual();
            }
            else if (_drawing is MosaicAnnotation mo)
            {
                mo.Bounds = new Rect(_dragStartImagePoint, p);
                InvalidateVisual();
            }
            else if (_drawing is ArrowAnnotation ar)
            {
                ar.End = p;
                InvalidateVisual();
            }
            else if (_drawing is FreehandAnnotation fh)
            {
                AddFreehandPoint(fh, p);
                InvalidateVisual();
            }
            else if (_isMovingSelection && _selected != null)
            {
                if (!_selectionMoved)
                {
                    PushUndo();
                    _selectionMoved = true;
                }
                _selected.Move(p - _dragStartImagePoint);
                _dragStartImagePoint = p;
                InvalidateVisual();
            }
            else if (_cropDragHandle != CropHandle.None && _cropRect.HasValue)
            {
                Vector delta = p - _cropDragStartImagePoint;
                _cropRect = ResizeCropRect(_cropDragStartRect, _cropDragHandle, delta);
                InvalidateVisual();
            }
            else if (CurrentTool == AnnotationTool.Crop && _cropRect.HasValue)
            {
                Cursor = CursorForCropHandle(HitTestCropHandle(p, _cropRect.Value));
            }
        }

        private CropHandle HitTestCropHandle(Point p, Rect crop)
        {
            double tol = Math.Max(6, CropHandleScreenSize) / Math.Max(0.05, Scale);
            bool nearLeft = Math.Abs(p.X - crop.Left) <= tol;
            bool nearRight = Math.Abs(p.X - crop.Right) <= tol;
            bool nearTop = Math.Abs(p.Y - crop.Top) <= tol;
            bool nearBottom = Math.Abs(p.Y - crop.Bottom) <= tol;
            bool midX = Math.Abs(p.X - (crop.Left + crop.Width / 2)) <= tol && p.Y >= crop.Top - tol && p.Y <= crop.Bottom + tol;
            bool midY = Math.Abs(p.Y - (crop.Top + crop.Height / 2)) <= tol && p.X >= crop.Left - tol && p.X <= crop.Right + tol;

            if (nearLeft && nearTop) return CropHandle.NW;
            if (nearRight && nearTop) return CropHandle.NE;
            if (nearLeft && nearBottom) return CropHandle.SW;
            if (nearRight && nearBottom) return CropHandle.SE;
            if (nearTop && midX) return CropHandle.N;
            if (nearBottom && midX) return CropHandle.S;
            if (nearLeft && midY) return CropHandle.W;
            if (nearRight && midY) return CropHandle.E;
            if (crop.Contains(p)) return CropHandle.Move;
            return CropHandle.None;
        }

        private static Cursor CursorForCropHandle(CropHandle handle)
        {
            switch (handle)
            {
                case CropHandle.N:
                case CropHandle.S:
                    return Cursors.SizeNS;
                case CropHandle.E:
                case CropHandle.W:
                    return Cursors.SizeWE;
                case CropHandle.NE:
                case CropHandle.SW:
                    return Cursors.SizeNESW;
                case CropHandle.NW:
                case CropHandle.SE:
                    return Cursors.SizeNWSE;
                case CropHandle.Move:
                    return Cursors.SizeAll;
                default:
                    return Cursors.Cross;
            }
        }

        private void SnapSingleWatermarkToGrid()
        {
            if (Image == null || _watermark == null) return;
            double threshold = 10.0 / Math.Max(0.05, Scale);
            double centerX = Image.PixelWidth / 2.0 + _watermark.HorizontalOffset;
            double centerY = Image.PixelHeight / 2.0 + _watermark.VerticalOffset;
            centerX = SnapCoordinate(centerX, Image.PixelWidth, threshold);
            centerY = SnapCoordinate(centerY, Image.PixelHeight, threshold);
            _watermark.HorizontalOffset = centerX - Image.PixelWidth / 2.0;
            _watermark.VerticalOffset = centerY - Image.PixelHeight / 2.0;
        }

        private static double SnapCoordinate(double value, double length, double threshold)
        {
            if (length <= 0) return value;
            double best = value;
            double bestDistance = threshold;
            for (int i = 0; i <= 10; i++)
            {
                double candidate = length * i / 10.0;
                double distance = Math.Abs(value - candidate);
                if (distance <= bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }
            double[] extra = { length / 3.0, length * 2.0 / 3.0, length / 2.0 };
            foreach (double candidate in extra)
            {
                double distance = Math.Abs(value - candidate);
                if (distance <= bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private Rect ResizeCropRect(Rect start, CropHandle handle, Vector delta)
        {
            double imgW = Image.PixelWidth;
            double imgH = Image.PixelHeight;

            if (handle == CropHandle.Move)
            {
                double x = Math.Max(0, Math.Min(start.X + delta.X, imgW - start.Width));
                double y = Math.Max(0, Math.Min(start.Y + delta.Y, imgH - start.Height));
                return new Rect(x, y, start.Width, start.Height);
            }

            double left = start.Left;
            double top = start.Top;
            double right = start.Right;
            double bottom = start.Bottom;
            bool affectsLeft = handle == CropHandle.W || handle == CropHandle.NW || handle == CropHandle.SW;
            bool affectsRight = handle == CropHandle.E || handle == CropHandle.NE || handle == CropHandle.SE;
            bool affectsTop = handle == CropHandle.N || handle == CropHandle.NW || handle == CropHandle.NE;
            bool affectsBottom = handle == CropHandle.S || handle == CropHandle.SW || handle == CropHandle.SE;

            if (affectsLeft) left = Math.Max(0, Math.Min(left + delta.X, right - 8));
            if (affectsRight) right = Math.Min(imgW, Math.Max(right + delta.X, left + 8));
            if (affectsTop) top = Math.Max(0, Math.Min(top + delta.Y, bottom - 8));
            if (affectsBottom) bottom = Math.Min(imgH, Math.Max(bottom + delta.Y, top + 8));

            var result = new Rect(new Point(left, top), new Point(right, bottom));
            if (!_cropAspectRatio.HasValue) return result;

            double aspect = _cropAspectRatio.Value;
            bool horizontalDrag = affectsLeft || affectsRight;
            bool verticalDrag = affectsTop || affectsBottom;
            if (horizontalDrag && !verticalDrag)
            {
                double h = result.Width / aspect;
                double y = affectsTop ? result.Bottom - h : result.Top;
                y = Math.Max(0, Math.Min(y, imgH - h));
                return new Rect(result.X, y, result.Width, Math.Min(h, imgH - y));
            }

            double w = result.Height * aspect;
            double x2 = affectsLeft ? result.Right - w : result.Left;
            x2 = Math.Max(0, Math.Min(x2, imgW - w));
            return new Rect(x2, result.Y, Math.Min(w, imgW - x2), result.Height);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!IsEditingEnabled) return;
            if (_isMovingSingleWatermark)
            {
                EndWatermarkDrag();
                e.Handled = true;
                return;
            }
            if (_drawing != null)
            {
                if (_drawing is FreehandAnnotation upFreehand && IsFreehandIdleLongEnough())
                    _drawing = TryCreateBeautifiedShape(upFreehand) ?? _drawing;
                _beautifyTimer.Stop();
                ReleaseMouseCapture();
                if (IsDrawingValid(_drawing))
                {
                    PushUndo();
                    Annotations.Add(_drawing);
                    AnnotationsChanged?.Invoke(this, EventArgs.Empty);
                }
                _drawing = null;
                InvalidateVisual();
            }
            else if (_isMovingSelection)
            {
                _beautifyTimer.Stop();
                _isMovingSelection = false;
                bool moved = _selectionMoved;
                _selectionMoved = false;
                ReleaseMouseCapture();
                if (moved) AnnotationsChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (_cropDragHandle != CropHandle.None)
            {
                _cropDragHandle = CropHandle.None;
                ReleaseMouseCapture();
                InvalidateVisual();
            }
        }

        private void StartBeautifyWatch()
        {
            _lastFreehandStrokeAt = DateTime.UtcNow;
            _beautifyTimer.Start();
        }

        private void AddFreehandPoint(FreehandAnnotation freehand, Point point)
        {
            if (freehand.Points.Count == 0)
            {
                freehand.Points.Add(point);
                _lastFreehandStrokeAt = DateTime.UtcNow;
                return;
            }

            Point last = freehand.Points[freehand.Points.Count - 1];
            if ((point - last).Length < Math.Max(0.8, CurrentThickness * 0.08)) return;

            freehand.Points.Add(point);
            _lastFreehandStrokeAt = DateTime.UtcNow;
        }

        private void BeautifyTimer_Tick(object sender, EventArgs e)
        {
            if (!IsMouseCaptured || !(_drawing is FreehandAnnotation freehand)) return;
            if (!IsFreehandIdleLongEnough()) return;

            Annotation beautified = TryCreateBeautifiedShape(freehand);
            if (beautified == null) return;

            _beautifyTimer.Stop();
            _drawing = beautified;
            InvalidateVisual();
        }

        private bool IsFreehandIdleLongEnough() =>
            (DateTime.UtcNow - _lastFreehandStrokeAt).TotalMilliseconds >= 1050;

        private Annotation TryCreateBeautifiedShape(FreehandAnnotation freehand)
        {
            if (freehand.Points.Count < 8) return null;

            Rect bounds = NormalizeRect(freehand.GetBounds());
            if (bounds.Width < 12 || bounds.Height < 12) return null;

            Point first = freehand.Points[0];
            Point last = freehand.Points[freehand.Points.Count - 1];
            double diagonal = Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height);
            double closeDistance = (last - first).Length;
            double pathLength = GetPathLength(freehand.Points);
            double directLength = closeDistance;
            double rectangleError = GetRectangleFitError(freehand.Points, bounds);
            double ellipseError = GetEllipseFitError(freehand.Points, bounds);
            double ellipseSweep = GetEllipseSweep(freehand.Points, bounds);
            double ellipseCoverage = Math.Abs(ellipseSweep);
            int coveredCorners = CountCoveredRectangleCorners(freehand.Points, bounds);
            double rectangleSupport = GetRectangleSupport(freehand.Points, bounds);
            double ellipseSupport = GetEllipseSupport(freehand.Points, bounds);
            double rectangleScore = rectangleSupport * 0.72 + coveredCorners / 4.0 * 0.28;
            double ellipseScore = ellipseSupport;

            bool rectangleLike =
                closeDistance <= diagonal * 0.38 &&
                coveredCorners >= 3 &&
                rectangleSupport >= 0.68 &&
                rectangleError <= 0.16 &&
                rectangleScore >= ellipseScore + 0.06;
            if (rectangleLike)
            {
                var organicRectangle = new OrganicRectAnnotation
                {
                    Bounds = bounds,
                    StrokeColor = freehand.StrokeColor,
                    Thickness = freehand.Thickness
                };
                organicRectangle.Points.AddRange(BuildOrganicRectangleContour(freehand.Points, bounds, freehand.Thickness));
                return organicRectangle;
            }

            var polygonVertices = TryGetPolygonVertices(
                freehand.Points, bounds, closeDistance, diagonal, ellipseSupport);
            if (polygonVertices != null)
            {
                var polygon = new OrganicPolygonAnnotation
                {
                    StrokeColor = freehand.StrokeColor,
                    Thickness = freehand.Thickness
                };
                polygon.Vertices.AddRange(polygonVertices);
                return polygon;
            }

            bool ellipseLike =
                ellipseCoverage >= Math.PI * 1.25 &&
                ellipseSupport >= 0.64 &&
                ellipseError <= 0.3 &&
                (coveredCorners < 3 || ellipseScore >= rectangleScore + 0.04);
            if (ellipseLike)
            {
                Rect ellipseBounds = SnapNearlyRoundBounds(bounds);
                double startAngle = GetEllipseAngle(first, ellipseBounds);
                double endAngle = GetEllipseAngle(last, ellipseBounds);
                double endpointGap = GetSmallestAngleDistance(startAngle, endAngle);
                bool leaveOpen =
                    closeDistance > diagonal * 0.08 &&
                    endpointGap >= 0.16;

                if (leaveOpen)
                {
                    double direction = Math.Sign(ellipseSweep);
                    if (direction == 0) direction = 1;
                    double sweep = direction * (Math.PI * 2 - endpointGap);
                    var openEllipse = new OpenEllipseAnnotation
                    {
                        Bounds = ellipseBounds,
                        StartAngle = startAngle,
                        SweepAngle = sweep,
                        StrokeColor = freehand.StrokeColor,
                        Thickness = freehand.Thickness
                    };
                    openEllipse.Points.AddRange(BuildOrganicEllipseContour(freehand.Points, ellipseBounds));
                    return openEllipse;
                }

                return new EllipseAnnotation
                {
                    Bounds = ellipseBounds,
                    StrokeColor = freehand.StrokeColor,
                    Thickness = freehand.Thickness
                };
            }

            bool classificationAmbiguous =
                closeDistance <= diagonal * 0.38 &&
                Math.Abs(rectangleScore - ellipseScore) < 0.08;
            if (classificationAmbiguous)
                return null;

            if (closeDistance <= diagonal * 0.28)
            {
                double boxArea = bounds.Width * bounds.Height;
                double fillRatio = boxArea > 0 ? GetPolygonArea(freehand.Points) / boxArea : 1.0;
                if (fillRatio > 0.9 && coveredCorners >= 3 && rectangleSupport >= 0.62)
                {
                    var organicRectangle = new OrganicRectAnnotation
                    {
                        Bounds = bounds,
                        StrokeColor = freehand.StrokeColor,
                        Thickness = freehand.Thickness
                    };
                    organicRectangle.Points.AddRange(BuildOrganicRectangleContour(freehand.Points, bounds, freehand.Thickness));
                    return organicRectangle;
                }
            }

            if (directLength >= 28 && pathLength > 0 && directLength / pathLength >= 0.42)
            {
                return new ArrowAnnotation
                {
                    Start = first,
                    End = last,
                    StrokeColor = freehand.StrokeColor,
                    Thickness = freehand.Thickness,
                    Style = CurrentArrowStyle
                };
            }

            return null;
        }

        private static double GetRectangleSupport(IList<Point> points, Rect bounds)
        {
            double width = Math.Max(1, bounds.Width);
            double height = Math.Max(1, bounds.Height);
            int supported = 0;
            foreach (Point point in points)
            {
                double nearestEdge = Math.Min(
                    Math.Min(Math.Abs(point.X - bounds.Left) / width, Math.Abs(bounds.Right - point.X) / width),
                    Math.Min(Math.Abs(point.Y - bounds.Top) / height, Math.Abs(bounds.Bottom - point.Y) / height));
                if (nearestEdge <= 0.095) supported++;
            }
            return supported / (double)Math.Max(1, points.Count);
        }

        private static double GetEllipseSupport(IList<Point> points, Rect bounds)
        {
            double rx = Math.Max(1, bounds.Width / 2);
            double ry = Math.Max(1, bounds.Height / 2);
            double cx = bounds.X + rx;
            double cy = bounds.Y + ry;
            int supported = 0;
            foreach (Point point in points)
            {
                double nx = (point.X - cx) / rx;
                double ny = (point.Y - cy) / ry;
                double radialError = Math.Abs(Math.Sqrt(nx * nx + ny * ny) - 1);
                if (radialError <= 0.13) supported++;
            }
            return supported / (double)Math.Max(1, points.Count);
        }

        private static List<Point> TryGetPolygonVertices(
            IList<Point> source,
            Rect bounds,
            double closeDistance,
            double diagonal,
            double ellipseSupport)
        {
            if (source.Count < 10 || closeDistance > diagonal * 0.25 || ellipseSupport >= 0.79)
                return null;

            var closed = new List<Point>(source);
            if ((closed[closed.Count - 1] - closed[0]).Length <= diagonal * 0.12)
                closed.RemoveAt(closed.Count - 1);
            if (closed.Count < 8) return null;

            Point centroid = new Point(closed.Average(point => point.X), closed.Average(point => point.Y));
            int startIndex = 0;
            double farthest = 0;
            for (int i = 0; i < closed.Count; i++)
            {
                double distance = (closed[i] - centroid).LengthSquared;
                if (distance > farthest)
                {
                    farthest = distance;
                    startIndex = i;
                }
            }

            var rotated = new List<Point>(closed.Count + 1);
            for (int i = 0; i < closed.Count; i++)
                rotated.Add(closed[(startIndex + i) % closed.Count]);
            rotated.Add(rotated[0]);

            List<Point> simplified = SimplifyPolyline(rotated, diagonal * 0.035);
            if (simplified.Count > 1 &&
                (simplified[simplified.Count - 1] - simplified[0]).Length <= diagonal * 0.08)
                simplified.RemoveAt(simplified.Count - 1);

            MergeShortPolygonEdges(simplified, diagonal * 0.09);
            if (simplified.Count < 3 || simplified.Count > 6) return null;

            double support = GetPolygonLineSupport(source, simplified, diagonal, out double averageError);
            if (support < 0.82 || averageError > 0.03) return null;

            for (int i = 0; i < simplified.Count; i++)
            {
                Vector incoming = simplified[(i - 1 + simplified.Count) % simplified.Count] - simplified[i];
                Vector outgoing = simplified[(i + 1) % simplified.Count] - simplified[i];
                if (incoming.Length < diagonal * 0.08 || outgoing.Length < diagonal * 0.08)
                    return null;
                incoming.Normalize();
                outgoing.Normalize();
                double dot = Math.Max(-1, Math.Min(1, Vector.Multiply(incoming, outgoing)));
                double interiorAngle = Math.Acos(dot);
                if (interiorAngle < 0.35 || interiorAngle > 2.82)
                    return null;
            }

            return simplified;
        }

        private static List<Point> SimplifyPolyline(IList<Point> points, double tolerance)
        {
            if (points.Count <= 2) return new List<Point>(points);
            double maxDistance = 0;
            int split = -1;
            for (int i = 1; i < points.Count - 1; i++)
            {
                double distance = DistanceToSegment(points[i], points[0], points[points.Count - 1]);
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    split = i;
                }
            }

            if (maxDistance <= tolerance || split < 0)
                return new List<Point> { points[0], points[points.Count - 1] };

            var leftInput = new List<Point>();
            var rightInput = new List<Point>();
            for (int i = 0; i <= split; i++) leftInput.Add(points[i]);
            for (int i = split; i < points.Count; i++) rightInput.Add(points[i]);
            List<Point> left = SimplifyPolyline(leftInput, tolerance);
            List<Point> right = SimplifyPolyline(rightInput, tolerance);
            left.RemoveAt(left.Count - 1);
            left.AddRange(right);
            return left;
        }

        private static void MergeShortPolygonEdges(List<Point> vertices, double minimumLength)
        {
            bool changed = true;
            while (changed && vertices.Count > 3)
            {
                changed = false;
                for (int i = 0; i < vertices.Count; i++)
                {
                    int next = (i + 1) % vertices.Count;
                    if ((vertices[next] - vertices[i]).Length >= minimumLength) continue;
                    vertices.RemoveAt(next);
                    changed = true;
                    break;
                }
            }
        }

        private static double GetPolygonLineSupport(
            IList<Point> points,
            IList<Point> vertices,
            double diagonal,
            out double averageError)
        {
            int supported = 0;
            double total = 0;
            foreach (Point point in points)
            {
                double nearest = double.MaxValue;
                for (int i = 0; i < vertices.Count; i++)
                    nearest = Math.Min(nearest,
                        DistanceToSegment(point, vertices[i], vertices[(i + 1) % vertices.Count]));
                double normalized = nearest / Math.Max(1, diagonal);
                total += normalized;
                if (normalized <= 0.045) supported++;
            }
            averageError = total / Math.Max(1, points.Count);
            return supported / (double)Math.Max(1, points.Count);
        }

        private static double DistanceToSegment(Point point, Point start, Point end)
        {
            Vector segment = end - start;
            double lengthSquared = segment.LengthSquared;
            if (lengthSquared <= 0.0001) return (point - start).Length;
            double t = Vector.Multiply(point - start, segment) / lengthSquared;
            t = Math.Max(0, Math.Min(1, t));
            return (point - (start + segment * t)).Length;
        }

        private static List<Point> BuildOrganicRectangleContour(
            IList<Point> source,
            Rect bounds,
            double thickness)
        {
            double minSide = Math.Min(bounds.Width, bounds.Height);
            double radius = Math.Max(7, Math.Min(minSide * 0.14, minSide * 0.055 + thickness * 0.75));
            double[] inset = GetRectangleSideInsets(source, bounds);

            double topBow = Math.Min(radius * 0.32, inset[0] * 0.32);
            double rightBow = Math.Min(radius * 0.32, inset[1] * 0.32);
            double bottomBow = Math.Min(radius * 0.32, inset[2] * 0.32);
            double leftBow = Math.Min(radius * 0.32, inset[3] * 0.32);

            double topLeftRadius = radius * (0.92 + Math.Min(0.12, leftBow / Math.Max(1, radius)));
            double topRightRadius = radius * (1.03 - Math.Min(0.1, topBow / Math.Max(1, radius)));
            double bottomRightRadius = radius * (0.94 + Math.Min(0.12, rightBow / Math.Max(1, radius)));
            double bottomLeftRadius = radius * (1.04 - Math.Min(0.1, bottomBow / Math.Max(1, radius)));

            var points = new List<Point>(52);
            AddOrganicSide(points,
                new Point(bounds.Left + topLeftRadius, bounds.Top),
                new Point(bounds.Right - topRightRadius, bounds.Top),
                new Vector(0, topBow), 7);
            AddCorner(points, new Point(bounds.Right - topRightRadius, bounds.Top + topRightRadius),
                topRightRadius, -Math.PI / 2, 0, 5);
            AddOrganicSide(points,
                new Point(bounds.Right, bounds.Top + topRightRadius),
                new Point(bounds.Right, bounds.Bottom - bottomRightRadius),
                new Vector(-rightBow, 0), 7);
            AddCorner(points, new Point(bounds.Right - bottomRightRadius, bounds.Bottom - bottomRightRadius),
                bottomRightRadius, 0, Math.PI / 2, 5);
            AddOrganicSide(points,
                new Point(bounds.Right - bottomRightRadius, bounds.Bottom),
                new Point(bounds.Left + bottomLeftRadius, bounds.Bottom),
                new Vector(0, -bottomBow), 7);
            AddCorner(points, new Point(bounds.Left + bottomLeftRadius, bounds.Bottom - bottomLeftRadius),
                bottomLeftRadius, Math.PI / 2, Math.PI, 5);
            AddOrganicSide(points,
                new Point(bounds.Left, bounds.Bottom - bottomLeftRadius),
                new Point(bounds.Left, bounds.Top + topLeftRadius),
                new Vector(leftBow, 0), 7);
            AddCorner(points, new Point(bounds.Left + topLeftRadius, bounds.Top + topLeftRadius),
                topLeftRadius, Math.PI, Math.PI * 1.5, 5);
            return points;
        }

        private static double[] GetRectangleSideInsets(IList<Point> points, Rect bounds)
        {
            var sums = new double[4];
            var counts = new int[4];
            double width = Math.Max(1, bounds.Width);
            double height = Math.Max(1, bounds.Height);

            foreach (Point point in points)
            {
                double[] distances =
                {
                    Math.Abs(point.Y - bounds.Top) / height,
                    Math.Abs(bounds.Right - point.X) / width,
                    Math.Abs(bounds.Bottom - point.Y) / height,
                    Math.Abs(point.X - bounds.Left) / width
                };
                int side = 0;
                for (int i = 1; i < distances.Length; i++)
                    if (distances[i] < distances[side]) side = i;

                double inset = side == 0 ? point.Y - bounds.Top :
                    side == 1 ? bounds.Right - point.X :
                    side == 2 ? bounds.Bottom - point.Y :
                    point.X - bounds.Left;
                sums[side] += Math.Max(0, inset);
                counts[side]++;
            }

            for (int i = 0; i < sums.Length; i++)
                sums[i] = counts[i] == 0 ? 0 : sums[i] / counts[i];
            return sums;
        }

        private static void AddOrganicSide(
            ICollection<Point> points,
            Point start,
            Point end,
            Vector bow,
            int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                double t = i / (double)segments;
                double curve = Math.Sin(t * Math.PI);
                points.Add(new Point(
                    start.X + (end.X - start.X) * t + bow.X * curve,
                    start.Y + (end.Y - start.Y) * t + bow.Y * curve));
            }
        }

        private static void AddCorner(
            ICollection<Point> points,
            Point center,
            double radius,
            double startAngle,
            double endAngle,
            int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                double t = i / (double)segments;
                double angle = startAngle + (endAngle - startAngle) * t;
                points.Add(new Point(
                    center.X + Math.Cos(angle) * radius,
                    center.Y + Math.Sin(angle) * radius));
            }
        }

        private static List<Point> BuildOrganicEllipseContour(IList<Point> source, Rect ellipseBounds)
        {
            var contour = new List<Point>(source.Count);
            double rx = Math.Max(1, ellipseBounds.Width / 2);
            double ry = Math.Max(1, ellipseBounds.Height / 2);
            double cx = ellipseBounds.X + rx;
            double cy = ellipseBounds.Y + ry;

            for (int i = 0; i < source.Count; i++)
            {
                Point point = source[i];
                double angle = Math.Atan2((point.Y - cy) / ry, (point.X - cx) / rx);
                var ideal = new Point(cx + Math.Cos(angle) * rx, cy + Math.Sin(angle) * ry);

                // Keep more of the original gesture near the tips, where a human stroke is least regular.
                double edgeDistance = Math.Min(i, source.Count - 1 - i) / (double)Math.Max(1, source.Count - 1);
                double originalWeight = 0.46 - Math.Min(0.14, edgeDistance * 0.45);
                contour.Add(new Point(
                    ideal.X * (1 - originalWeight) + point.X * originalWeight,
                    ideal.Y * (1 - originalWeight) + point.Y * originalWeight));
            }

            for (int pass = 0; pass < 2; pass++)
            {
                var smoothed = new List<Point>(contour);
                for (int i = 1; i < contour.Count - 1; i++)
                {
                    smoothed[i] = new Point(
                        contour[i - 1].X * 0.22 + contour[i].X * 0.56 + contour[i + 1].X * 0.22,
                        contour[i - 1].Y * 0.22 + contour[i].Y * 0.56 + contour[i + 1].Y * 0.22);
                }
                contour = smoothed;
            }

            return contour;
        }

        private static int CountCoveredRectangleCorners(IList<Point> points, Rect bounds)
        {
            double width = Math.Max(1, bounds.Width);
            double height = Math.Max(1, bounds.Height);
            var corners = new[]
            {
                new Point(bounds.Left, bounds.Top),
                new Point(bounds.Right, bounds.Top),
                new Point(bounds.Right, bounds.Bottom),
                new Point(bounds.Left, bounds.Bottom)
            };

            int covered = 0;
            foreach (Point corner in corners)
            {
                bool found = points.Any(point =>
                {
                    double dx = (point.X - corner.X) / width;
                    double dy = (point.Y - corner.Y) / height;
                    return Math.Sqrt(dx * dx + dy * dy) <= 0.14;
                });
                if (found) covered++;
            }
            return covered;
        }

        private static double GetRectangleFitError(IList<Point> points, Rect bounds)
        {
            double width = Math.Max(1, bounds.Width);
            double height = Math.Max(1, bounds.Height);
            double total = 0;
            foreach (Point point in points)
            {
                double left = Math.Abs(point.X - bounds.Left) / width;
                double right = Math.Abs(bounds.Right - point.X) / width;
                double top = Math.Abs(point.Y - bounds.Top) / height;
                double bottom = Math.Abs(bounds.Bottom - point.Y) / height;
                total += Math.Min(Math.Min(left, right), Math.Min(top, bottom));
            }
            return total / Math.Max(1, points.Count);
        }

        private static double GetEllipseFitError(IList<Point> points, Rect bounds)
        {
            double rx = Math.Max(1, bounds.Width / 2);
            double ry = Math.Max(1, bounds.Height / 2);
            double cx = bounds.X + rx;
            double cy = bounds.Y + ry;
            double total = 0;
            foreach (Point point in points)
            {
                double nx = (point.X - cx) / rx;
                double ny = (point.Y - cy) / ry;
                total += Math.Abs(Math.Sqrt(nx * nx + ny * ny) - 1);
            }
            return total / Math.Max(1, points.Count);
        }

        private static double GetEllipseSweep(IList<Point> points, Rect bounds)
        {
            if (points.Count < 2) return 0;
            double total = 0;
            double previous = GetEllipseAngle(points[0], bounds);
            for (int i = 1; i < points.Count; i++)
            {
                double current = GetEllipseAngle(points[i], bounds);
                double delta = current - previous;
                while (delta > Math.PI) delta -= Math.PI * 2;
                while (delta < -Math.PI) delta += Math.PI * 2;
                total += delta;
                previous = current;
            }
            return total;
        }

        private static double GetEllipseAngle(Point point, Rect bounds)
        {
            double rx = Math.Max(1, bounds.Width / 2);
            double ry = Math.Max(1, bounds.Height / 2);
            double cx = bounds.X + rx;
            double cy = bounds.Y + ry;
            return Math.Atan2((point.Y - cy) / ry, (point.X - cx) / rx);
        }

        private static double GetSmallestAngleDistance(double first, double second)
        {
            double difference = Math.Abs(first - second) % (Math.PI * 2);
            return Math.Min(difference, Math.PI * 2 - difference);
        }

        private static Rect SnapNearlyRoundBounds(Rect bounds)
        {
            double aspect = bounds.Width / Math.Max(1, bounds.Height);
            if (aspect < 0.88 || aspect > 1.14) return bounds;
            double size = (bounds.Width + bounds.Height) / 2;
            var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            return new Rect(center.X - size / 2, center.Y - size / 2, size, size);
        }

        private static double GetPolygonArea(IList<Point> points)
        {
            double area = 0;
            int n = points.Count;
            for (int i = 0; i < n; i++)
            {
                Point a = points[i];
                Point b = points[(i + 1) % n];
                area += a.X * b.Y - b.X * a.Y;
            }
            return Math.Abs(area) / 2.0;
        }

        private static double GetPathLength(IList<Point> points)
        {
            double length = 0;
            for (int i = 1; i < points.Count; i++)
                length += (points[i] - points[i - 1]).Length;
            return length;
        }

        private static Rect NormalizeRect(Rect rect)
        {
            double x = Math.Min(rect.Left, rect.Right);
            double y = Math.Min(rect.Top, rect.Bottom);
            double width = Math.Abs(rect.Width);
            double height = Math.Abs(rect.Height);
            return new Rect(x, y, width, height);
        }

        private static Rect BuildSquareRect(Point start, Point end)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double size = Math.Min(Math.Abs(dx), Math.Abs(dy));
            return new Rect(start, new Point(
                start.X + Math.Sign(dx == 0 ? 1 : dx) * size,
                start.Y + Math.Sign(dy == 0 ? 1 : dy) * size));
        }

        private static bool IsDrawingValid(Annotation a)
        {
            if (a is ArrowAnnotation ar) return (ar.End - ar.Start).Length >= 4;
            if (a is FreehandAnnotation fh) return fh.Points.Count >= 2;
            var b = a.GetBounds();
            return b.Width >= 2 && b.Height >= 2;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!IsEditingEnabled)
            {
                base.OnKeyDown(e);
                return;
            }

            if (_editingText != null)
            {
                if (e.Key == Key.Enter)
                {
                    CommitTextEdit();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    CancelTextEdit();
                    e.Handled = true;
                }
                else if (e.Key == Key.Back)
                {
                    if (_editingText.Text.Length > 0)
                        _editingText.Text = _editingText.Text.Substring(0, _editingText.Text.Length - 1);
                    InvalidateVisual();
                    AnnotationsChanged?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                }
                return;
            }

            if (e.Key == Key.Delete)
            {
                DeleteSelected();
            }
            else if (e.Key == Key.Escape)
            {
                SetSelected(null);
                InvalidateVisual();
            }
        }

        protected override void OnTextInput(TextCompositionEventArgs e)
        {
            if (!IsEditingEnabled)
            {
                base.OnTextInput(e);
                return;
            }

            if (_editingText == null)
            {
                base.OnTextInput(e);
                return;
            }

            if (!string.IsNullOrEmpty(e.Text))
            {
                _editingText.Text += e.Text;
                InvalidateVisual();
                AnnotationsChanged?.Invoke(this, EventArgs.Empty);
            }
            e.Handled = true;
        }

        public void DeleteSelected()
        {
            if (!IsEditingEnabled) return;
            if (_selected == null) return;
            PushUndo();
            Annotations.Remove(_selected);
            SetSelected(null);
            InvalidateVisual();
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Deselect()
        {
            if (_selected == null) return;
            SetSelected(null);
            InvalidateVisual();
        }

        public void SetSelectedColor(Color color)
        {
            if (!IsEditingEnabled) return;
            if (_selected == null) return;
            PushUndo();
            _selected.StrokeColor = color;
            InvalidateVisual();
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetSelectedThickness(double thickness)
        {
            if (!IsEditingEnabled) return;
            if (_selected == null || _selected is TextAnnotation || _selected is MosaicAnnotation) return;
            PushUndo();
            _selected.Thickness = thickness;
            InvalidateVisual();
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void AdjustSelectedFontSize(double delta)
        {
            if (!IsEditingEnabled) return;
            if (!(_selected is TextAnnotation text)) return;
            PushUndo();
            text.FontSize = Math.Max(12, Math.Min(160, text.FontSize + delta));
            InvalidateVisual();
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetSelectedFontSize(double fontSize)
        {
            if (!IsEditingEnabled) return;
            if (!(_selected is TextAnnotation text)) return;
            PushUndo();
            text.FontSize = Math.Max(12, Math.Min(160, fontSize));
            InvalidateVisual();
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void EditSelectedText(string newText, double fontSize)
        {
            if (!IsEditingEnabled) return;
            if (!(_selected is TextAnnotation text) || string.IsNullOrWhiteSpace(newText)) return;
            PushUndo();
            text.Text = newText;
            text.FontSize = Math.Max(12, Math.Min(160, fontSize));
            InvalidateVisual();
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
        }

        private Annotation HitTest(Point imagePoint)
        {
            for (int i = Annotations.Count - 1; i >= 0; i--)
            {
                var b = Annotations[i].GetBounds();
                b.Inflate(8, 8);
                if (b.Contains(imagePoint)) return Annotations[i];
            }
            return null;
        }

        public TextAnnotation AddTextAnnotation(Point imagePoint, string text)
        {
            if (!IsEditingEnabled) return null;
            if (string.IsNullOrWhiteSpace(text)) return null;
            PushUndo();
            var annotation = new TextAnnotation
            {
                Location = imagePoint,
                Text = text,
                StrokeColor = CurrentColor,
                FontSize = CurrentFontSize
            };
            Annotations.Add(annotation);
            SetSelected(annotation);
            InvalidateVisual();
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
            return annotation;
        }

        public void BeginTextAnnotation(Point imagePoint)
        {
            if (!IsEditingEnabled) return;
            PushUndo();
            var annotation = new TextAnnotation
            {
                Location = imagePoint,
                Text = string.Empty,
                StrokeColor = CurrentColor,
                FontSize = CurrentFontSize
            };
            Annotations.Add(annotation);
            _editingText = annotation;
            _editingTextWasNew = true;
            _editingOriginalText = string.Empty;
            _editingOriginalFontSize = annotation.FontSize;
            SetSelected(annotation);
            ShowTextEditor();
            InvalidateVisual();
        }

        private void BeginEditTextAnnotation(TextAnnotation annotation)
        {
            if (!IsEditingEnabled) return;
            PushUndo();
            _editingText = annotation;
            _editingTextWasNew = false;
            _editingOriginalText = annotation.Text;
            _editingOriginalFontSize = annotation.FontSize;
            SetSelected(annotation);
            ShowTextEditor();
            InvalidateVisual();
        }

        private void ShowTextEditor()
        {
            RemoveTextEditor();
            if (_editingText == null) return;

            _textEditor = new TextBox
            {
                Text = _editingText.Text,
                FontFamily = new FontFamily(DefaultFontFamily),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(_editingText.StrokeColor),
                Background = new SolidColorBrush(Color.FromArgb(238, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(82, 101, 255)),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(8, 4, 8, 4),
                AcceptsReturn = false,
                TextWrapping = TextWrapping.NoWrap,
                MinWidth = 140,
                MinHeight = 34
            };
            _textEditor.TextChanged += TextEditor_TextChanged;
            _textEditor.PreviewKeyDown += TextEditor_PreviewKeyDown;
            _textEditor.LostKeyboardFocus += TextEditor_LostKeyboardFocus;
            Children.Add(_textEditor);
            PositionTextEditor();
            Dispatcher.BeginInvoke((Action)(() =>
            {
                if (_textEditor == null) return;
                _textEditor.Focus();
                Keyboard.Focus(_textEditor);
                _textEditor.CaretIndex = _textEditor.Text.Length;
            }), DispatcherPriority.Input);
        }

        private void TextEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_editingText == null || _textEditor == null) return;
            _editingText.Text = _textEditor.Text;
            InvalidateVisual();
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void TextEditor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitTextEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelTextEdit();
                e.Handled = true;
            }
        }

        private void TextEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            Dispatcher.BeginInvoke((Action)(() =>
            {
                if (_textEditor != null && !_textEditor.IsKeyboardFocusWithin)
                    CommitTextEdit();
            }), DispatcherPriority.Background);
        }

        private void PositionTextEditor()
        {
            if (_textEditor == null || _editingText == null) return;
            double scaledFontSize = Math.Max(12, _editingText.FontSize * Scale);
            _textEditor.FontSize = scaledFontSize;
            _textEditor.MinWidth = Math.Max(140, 240 * Scale);
            _textEditor.MinHeight = Math.Max(34, scaledFontSize * 1.6);
            _textEditor.MaxWidth = Image == null
                ? 600
                : Math.Max(180, (Image.PixelWidth - _editingText.Location.X) * Scale - 20);
            SetLeft(_textEditor, _editingText.Location.X * Scale);
            SetTop(_textEditor, _editingText.Location.Y * Scale);
        }

        private void RemoveTextEditor()
        {
            if (_textEditor == null) return;
            _textEditor.TextChanged -= TextEditor_TextChanged;
            _textEditor.PreviewKeyDown -= TextEditor_PreviewKeyDown;
            _textEditor.LostKeyboardFocus -= TextEditor_LostKeyboardFocus;
            Children.Remove(_textEditor);
            _textEditor = null;
            Focus();
        }

        private void CommitTextEdit()
        {
            if (_editingText == null) return;
            if (_textEditor != null)
                _editingText.Text = _textEditor.Text;
            if (string.IsNullOrWhiteSpace(_editingText.Text))
                Annotations.Remove(_editingText);
            RemoveTextEditor();
            _editingText = null;
            _editingTextWasNew = false;
            _editingOriginalText = null;
            InvalidateVisual();
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
            TextEditFinished?.Invoke(this, EventArgs.Empty);
        }

        private void CancelTextEdit()
        {
            if (_editingText == null) return;
            if (_editingTextWasNew || string.IsNullOrWhiteSpace(_editingText.Text))
                Annotations.Remove(_editingText);
            else
            {
                _editingText.Text = _editingOriginalText ?? string.Empty;
                _editingText.FontSize = _editingOriginalFontSize;
            }
            RemoveTextEditor();
            _editingText = null;
            _editingTextWasNew = false;
            _editingOriginalText = null;
            InvalidateVisual();
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
            TextEditFinished?.Invoke(this, EventArgs.Empty);
        }

        private void DrawEditingText(DrawingContext dc)
        {
            if (_editingText == null) return;
            string displayText = string.IsNullOrEmpty(_editingText.Text) ? "输入文字" : _editingText.Text + "│";
            var brush = string.IsNullOrEmpty(_editingText.Text)
                ? new SolidColorBrush(Color.FromArgb(150, 255, 255, 255))
                : new SolidColorBrush(_editingText.StrokeColor);
            var ft = new FormattedText(
                displayText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily(DefaultFontFamily), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                _editingText.FontSize,
                brush,
                1.0);

            var bounds = new Rect(_editingText.Location, new Size(Math.Max(ft.Width, 60), Math.Max(ft.Height, _editingText.FontSize + 8)));
            bounds.Inflate(10, 6);

            if (_cacheEditingBgBrush == null)
                _cacheEditingBgBrush = new SolidColorBrush(Color.FromArgb(80, 20, 20, 20));
            if (_cacheEditingPen == null)
                _cacheEditingPen = new Pen(new SolidColorBrush(Color.FromRgb(91, 108, 255)), 2);

            dc.DrawRoundedRectangle(_cacheEditingBgBrush, _cacheEditingPen, bounds, 4, 4);
            dc.DrawText(ft, _editingText.Location);
        }

        public void BeginCrop()
        {
            if (!IsEditingEnabled) return;
            if (Image == null) return;
            double marginX = Image.PixelWidth * 0.08;
            double marginY = Image.PixelHeight * 0.08;
            var rect = new Rect(marginX, marginY, Image.PixelWidth - marginX * 2, Image.PixelHeight - marginY * 2);
            _cropRect = _cropAspectRatio.HasValue ? FitAspect(rect, _cropAspectRatio.Value) : rect;
            InvalidateVisual();
        }

        public void CancelCrop()
        {
            if (_cropRect == null) return;
            _cropRect = null;
            _cropDragHandle = CropHandle.None;
            Cursor = null;
            InvalidateVisual();
        }

        public void SetCropAspectRatio(double? ratio)
        {
            if (!IsEditingEnabled) return;
            _cropAspectRatio = ratio;
            if (!_cropRect.HasValue || !ratio.HasValue) return;
            _cropRect = FitAspect(_cropRect.Value, ratio.Value);
            InvalidateVisual();
        }

        private Rect FitAspect(Rect bounds, double aspect)
        {
            double imgW = Image?.PixelWidth ?? bounds.Right;
            double imgH = Image?.PixelHeight ?? bounds.Bottom;
            var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            double width = bounds.Width;
            double height = width / aspect;
            if (height > bounds.Height)
            {
                height = bounds.Height;
                width = height * aspect;
            }
            double x = Math.Max(0, Math.Min(center.X - width / 2, imgW - width));
            double y = Math.Max(0, Math.Min(center.Y - height / 2, imgH - height));
            width = Math.Min(width, imgW - x);
            height = Math.Min(height, imgH - y);
            return new Rect(x, y, width, height);
        }

        public void ConfirmCrop()
        {
            if (!IsEditingEnabled) return;
            if (Image == null || !_cropRect.HasValue) return;
            Rect r = NormalizeRect(_cropRect.Value);
            int x = (int)Math.Round(Math.Max(0, r.X));
            int y = (int)Math.Round(Math.Max(0, r.Y));
            int w = (int)Math.Round(Math.Min(r.Width, Image.PixelWidth - x));
            int h = (int)Math.Round(Math.Min(r.Height, Image.PixelHeight - y));
            if (w < 8 || h < 8)
            {
                CancelCrop();
                return;
            }

            PushUndo();
            var cropped = new CroppedBitmap(Image, new Int32Rect(x, y, w, h));
            cropped.Freeze();
            var offset = new Vector(-x, -y);
            foreach (var annotation in Annotations)
                annotation.Move(offset);
            Image = cropped;
            _cropRect = null;
            _cropDragHandle = CropHandle.None;
            Cursor = null;
            SetSelected(null);
            InvalidateVisual();
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool ApplyImageTransform(ImageTransformOperation operation)
        {
            if (Image == null) return false;

            double oldWidth = Image.PixelWidth;
            double oldHeight = Image.PixelHeight;
            int newWidth = (operation == ImageTransformOperation.RotateLeft90 || operation == ImageTransformOperation.RotateRight90)
                ? Image.PixelHeight
                : Image.PixelWidth;
            int newHeight = (operation == ImageTransformOperation.RotateLeft90 || operation == ImageTransformOperation.RotateRight90)
                ? Image.PixelWidth
                : Image.PixelHeight;

            Matrix matrix = GetImageTransformMatrix(operation, oldWidth, oldHeight);
            Func<Point, Point> transformPoint = point => matrix.Transform(point);

            PushUndo();
            Image = RenderTransformedImage(Image, operation);
            var transformed = Annotations.Select(annotation => TransformAnnotation(annotation, transformPoint)).ToList();
            Annotations.Clear();
            Annotations.AddRange(transformed);
            TransformWatermark(operation, transformPoint, oldWidth, oldHeight, newWidth, newHeight);

            RemoveTextEditor();
            _editingText = null;
            _cropRect = null;
            _cropDragHandle = CropHandle.None;
            Cursor = null;
            SetSelected(null);
            InvalidateVisual();
            WatermarkChanged?.Invoke(this, EventArgs.Empty);
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private static Matrix GetImageTransformMatrix(ImageTransformOperation operation, double width, double height)
        {
            switch (operation)
            {
                case ImageTransformOperation.RotateLeft90:
                    return new Matrix(0, -1, 1, 0, 0, width);
                case ImageTransformOperation.RotateRight90:
                    return new Matrix(0, 1, -1, 0, height, 0);
                case ImageTransformOperation.Rotate180:
                    return new Matrix(-1, 0, 0, -1, width, height);
                case ImageTransformOperation.FlipHorizontal:
                    return new Matrix(-1, 0, 0, 1, width, 0);
                case ImageTransformOperation.FlipVertical:
                    return new Matrix(1, 0, 0, -1, 0, height);
                default:
                    return Matrix.Identity;
            }
        }

        private static BitmapSource RenderTransformedImage(BitmapSource source, ImageTransformOperation operation)
        {
            int sourceWidth = source.PixelWidth;
            int sourceHeight = source.PixelHeight;
            int targetWidth = (operation == ImageTransformOperation.RotateLeft90 || operation == ImageTransformOperation.RotateRight90)
                ? sourceHeight
                : sourceWidth;
            int targetHeight = (operation == ImageTransformOperation.RotateLeft90 || operation == ImageTransformOperation.RotateRight90)
                ? sourceWidth
                : sourceHeight;

            var converted = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int sourceStride = sourceWidth * 4;
            int targetStride = targetWidth * 4;
            var sourcePixels = new byte[sourceHeight * sourceStride];
            var targetPixels = new byte[targetHeight * targetStride];
            converted.CopyPixels(sourcePixels, sourceStride, 0);

            for (int y = 0; y < sourceHeight; y++)
            {
                int sourceRow = y * sourceStride;
                for (int x = 0; x < sourceWidth; x++)
                {
                    int targetX;
                    int targetY;
                    switch (operation)
                    {
                        case ImageTransformOperation.RotateLeft90:
                            targetX = y;
                            targetY = sourceWidth - 1 - x;
                            break;
                        case ImageTransformOperation.RotateRight90:
                            targetX = sourceHeight - 1 - y;
                            targetY = x;
                            break;
                        case ImageTransformOperation.Rotate180:
                            targetX = sourceWidth - 1 - x;
                            targetY = sourceHeight - 1 - y;
                            break;
                        case ImageTransformOperation.FlipHorizontal:
                            targetX = sourceWidth - 1 - x;
                            targetY = y;
                            break;
                        case ImageTransformOperation.FlipVertical:
                            targetX = x;
                            targetY = sourceHeight - 1 - y;
                            break;
                        default:
                            targetX = x;
                            targetY = y;
                            break;
                    }

                    int sourceIndex = sourceRow + x * 4;
                    int targetIndex = targetY * targetStride + targetX * 4;
                    targetPixels[targetIndex] = sourcePixels[sourceIndex];
                    targetPixels[targetIndex + 1] = sourcePixels[sourceIndex + 1];
                    targetPixels[targetIndex + 2] = sourcePixels[sourceIndex + 2];
                    targetPixels[targetIndex + 3] = sourcePixels[sourceIndex + 3];
                }
            }

            double dpiX = source.DpiX > 0 ? source.DpiX : 96;
            double dpiY = source.DpiY > 0 ? source.DpiY : 96;
            var bitmap = BitmapSource.Create(targetWidth, targetHeight, dpiX, dpiY, PixelFormats.Bgra32, null, targetPixels, targetStride);
            bitmap.Freeze();
            return bitmap;
        }

        private static Annotation TransformAnnotation(Annotation annotation, Func<Point, Point> transform)
        {
            if (annotation is RectAnnotation rect)
                return new RectAnnotation { Bounds = TransformRect(rect.Bounds, transform), StrokeColor = rect.StrokeColor, Thickness = rect.Thickness };

            if (annotation is OrganicRectAnnotation organicRect)
            {
                var clone = new OrganicRectAnnotation { Bounds = TransformRect(organicRect.Bounds, transform), StrokeColor = organicRect.StrokeColor, Thickness = organicRect.Thickness };
                clone.Points.AddRange(organicRect.Points.Select(transform));
                return clone;
            }

            if (annotation is OrganicPolygonAnnotation polygon)
            {
                var clone = new OrganicPolygonAnnotation { StrokeColor = polygon.StrokeColor, Thickness = polygon.Thickness };
                clone.Vertices.AddRange(polygon.Vertices.Select(transform));
                return clone;
            }

            if (annotation is EllipseAnnotation ellipse)
                return new EllipseAnnotation { Bounds = TransformRect(ellipse.Bounds, transform), StrokeColor = ellipse.StrokeColor, Thickness = ellipse.Thickness };

            if (annotation is OpenEllipseAnnotation openEllipse)
            {
                var clone = new OpenEllipseAnnotation
                {
                    Bounds = TransformRect(openEllipse.Bounds, transform),
                    StartAngle = openEllipse.StartAngle,
                    SweepAngle = openEllipse.SweepAngle,
                    StrokeColor = openEllipse.StrokeColor,
                    Thickness = openEllipse.Thickness
                };
                clone.Points.AddRange(openEllipse.Points.Select(transform));
                return clone;
            }

            if (annotation is ArrowAnnotation arrow)
                return new ArrowAnnotation { Start = transform(arrow.Start), End = transform(arrow.End), Style = arrow.Style, StrokeColor = arrow.StrokeColor, Thickness = arrow.Thickness };

            if (annotation is FreehandAnnotation freehand)
            {
                var clone = new FreehandAnnotation { StrokeColor = freehand.StrokeColor, Thickness = freehand.Thickness };
                clone.Points.AddRange(freehand.Points.Select(transform));
                return clone;
            }

            if (annotation is MosaicAnnotation mosaic)
                return new MosaicAnnotation { Bounds = TransformRect(mosaic.Bounds, transform), BlockSize = mosaic.BlockSize, Mode = mosaic.Mode, StrokeColor = mosaic.StrokeColor, Thickness = mosaic.Thickness };

            if (annotation is TextAnnotation text)
                return new TextAnnotation { Location = transform(text.Location), Text = text.Text, FontSize = text.FontSize, StrokeColor = text.StrokeColor, Thickness = text.Thickness };

            return annotation.Clone();
        }

        private static Rect TransformRect(Rect rect, Func<Point, Point> transform)
        {
            Point p1 = transform(new Point(rect.Left, rect.Top));
            Point p2 = transform(new Point(rect.Right, rect.Top));
            Point p3 = transform(new Point(rect.Right, rect.Bottom));
            Point p4 = transform(new Point(rect.Left, rect.Bottom));
            double left = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
            double top = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
            double right = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
            double bottom = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));
            return new Rect(new Point(left, top), new Point(right, bottom));
        }

        private void TransformWatermark(ImageTransformOperation operation, Func<Point, Point> transform, double oldWidth, double oldHeight, double newWidth, double newHeight)
        {
            if (_watermark == null) return;

            if (_watermark.Layout == WatermarkLayout.Single)
            {
                Point transformedCenter = transform(new Point(
                    oldWidth / 2.0 + _watermark.HorizontalOffset,
                    oldHeight / 2.0 + _watermark.VerticalOffset));
                _watermark.HorizontalOffset = transformedCenter.X - newWidth / 2.0;
                _watermark.VerticalOffset = transformedCenter.Y - newHeight / 2.0;
            }

            switch (operation)
            {
                case ImageTransformOperation.RotateLeft90:
                    _watermark.Angle = NormalizeAngle(_watermark.Angle - 90);
                    break;
                case ImageTransformOperation.RotateRight90:
                    _watermark.Angle = NormalizeAngle(_watermark.Angle + 90);
                    break;
                case ImageTransformOperation.Rotate180:
                    _watermark.Angle = NormalizeAngle(_watermark.Angle + 180);
                    break;
                case ImageTransformOperation.FlipHorizontal:
                    _watermark.Angle = NormalizeAngle(180 - _watermark.Angle);
                    break;
                case ImageTransformOperation.FlipVertical:
                    _watermark.Angle = NormalizeAngle(-_watermark.Angle);
                    break;
            }
        }

        private static double NormalizeAngle(double angle)
        {
            angle %= 360;
            if (angle > 180) angle -= 360;
            if (angle <= -180) angle += 360;
            return angle;
        }

        public void ClearAll()
        {
            _beautifyTimer.Stop();
            Annotations.Clear();
            _watermark = null;
            _isMovingSingleWatermark = false;
            SetSelected(null);
            _drawing = null;
            _isMovingSelection = false;
            _selectionMoved = false;
            _editingText = null;
            _editingTextWasNew = false;
            _editingOriginalText = null;
            RemoveTextEditor();
            _cropRect = null;
            _cropDragHandle = CropHandle.None;
            Cursor = null;
            _undo.Clear();
            InvalidateVisual();
        }

        public void LoadState(IEnumerable<Annotation> annotations, WatermarkSettings watermark)
        {
            _beautifyTimer.Stop();
            Annotations.Clear();
            if (annotations != null)
                Annotations.AddRange(annotations);
            _watermark = watermark?.Clone();
            _isMovingSingleWatermark = false;
            SetSelected(null);
            _drawing = null;
            _isMovingSelection = false;
            _selectionMoved = false;
            _editingText = null;
            _editingTextWasNew = false;
            _editingOriginalText = null;
            RemoveTextEditor();
            _cropRect = null;
            _cropDragHandle = CropHandle.None;
            Cursor = null;
            _undo.Clear();
            InvalidateVisual();
            WatermarkChanged?.Invoke(this, EventArgs.Empty);
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetWatermark(WatermarkSettings settings, bool notifyChanged = true, bool notifyWatermarkChanged = true)
        {
            _watermark = settings?.Clone();
            InvalidateVisual();
            if (notifyWatermarkChanged)
                WatermarkChanged?.Invoke(this, EventArgs.Empty);
            if (notifyChanged)
                AnnotationsChanged?.Invoke(this, EventArgs.Empty);
        }

        public WatermarkSettings GetWatermark() => _watermark?.Clone();

        private void PushUndo() => _undo.PushState(Image, Annotations, _watermark);

        public void Undo()
        {
            if (!IsEditingEnabled) return;
            var state = _undo.Undo(Image, Annotations, _watermark);
            if (state != null) ReplaceState(state);
        }

        public void Redo()
        {
            if (!IsEditingEnabled) return;
            var state = _undo.Redo(Image, Annotations, _watermark);
            if (state != null) ReplaceState(state);
        }

        private void ReplaceState(UndoManager.CanvasState state)
        {
            Image = state.Image;
            Annotations.Clear();
            Annotations.AddRange(state.Annotations);
            _watermark = state.Watermark?.Clone();
            RemoveTextEditor();
            _editingText = null;
            _cropRect = null;
            _cropDragHandle = CropHandle.None;
            Cursor = null;
            SetSelected(null);
            InvalidateVisual();
            WatermarkChanged?.Invoke(this, EventArgs.Empty);
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SetSelected(Annotation annotation)
        {
            if (ReferenceEquals(_selected, annotation)) return;
            _selected = annotation;
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public RenderTargetBitmap RenderFullResolution()
        {
            if (Image == null) return null;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(Image, new Rect(0, 0, Image.PixelWidth, Image.PixelHeight));
                var snapshot = Annotations.ToArray();
                foreach (var a in snapshot)
                    a.Draw(dc, false, Image);
                _watermark?.Draw(dc, Image);
            }
            var rtb = new RenderTargetBitmap(Image.PixelWidth, Image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            return rtb;
        }
    }
}
