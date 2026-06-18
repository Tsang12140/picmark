using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PicMark
{
    public class AnnotationCanvas : Canvas
    {
        private const string DefaultFontFamily = "Alibaba PuHuiTi 3.0, Alibaba PuHuiTi, Microsoft YaHei UI, Microsoft YaHei, SimHei, sans-serif";

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
            !string.IsNullOrWhiteSpace(_watermark.Text);

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
                        a.Draw(dc, a == _selected, Image);
                }
                _drawing?.Draw(dc, false, Image);
                _watermark?.Draw(dc, Image);
            }
            finally
            {
                dc.Pop();
            }
        }

        private Point ToImagePoint(Point screenPoint)
        {
            if (_scale <= 0) return new Point(0, 0);
            return new Point(screenPoint.X / Scale, screenPoint.Y / Scale);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (Image == null) return;
            Focus();
            var p = ToImagePoint(e.GetPosition(this));

            if (CurrentTool == AnnotationTool.Select &&
                _watermark != null &&
                _watermark.Layout == WatermarkLayout.Single &&
                _watermark.HitTestSingle(p, Image))
            {
                _isMovingSingleWatermark = true;
                _watermarkDragStartImagePoint = p;
                CaptureMouse();
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
            else if (_isMovingSingleWatermark && _watermark != null)
            {
                Vector delta = p - _watermarkDragStartImagePoint;
                _watermark.HorizontalOffset += delta.X;
                _watermark.VerticalOffset += delta.Y;
                _watermarkDragStartImagePoint = p;
                InvalidateVisual();
                WatermarkChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
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
            else if (_isMovingSingleWatermark)
            {
                _isMovingSingleWatermark = false;
                ReleaseMouseCapture();
                AnnotationsChanged?.Invoke(this, EventArgs.Empty);
                WatermarkChanged?.Invoke(this, EventArgs.Empty);
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

            if (closeDistance <= diagonal * 0.28)
            {
                // 用"画出来的实际面积 / 包围盒面积"判断是圆/椭圆还是矩形，
                // 而不是用宽高比——否则细长的椭圆会因为宽高比超出范围被误判成矩形。
                // 椭圆的理论填充率是 π/4≈0.785，手绘矩形（含转角圆润）通常在 0.9 以上。
                double boxArea = bounds.Width * bounds.Height;
                double fillRatio = boxArea > 0 ? GetPolygonArea(freehand.Points) / boxArea : 1.0;
                bool isRound = fillRatio <= 0.87;

                if (isRound)
                {
                    Rect ellipseBounds = bounds;
                    double aspect = bounds.Width / Math.Max(1, bounds.Height);
                    if (aspect >= 0.88 && aspect <= 1.14)
                    {
                        // 只有接近正圆时才吸附成正圆，避免把扁长椭圆强行拉成圆形
                        double size = (bounds.Width + bounds.Height) / 2;
                        var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                        ellipseBounds = new Rect(center.X - size / 2, center.Y - size / 2, size, size);
                    }
                    return new EllipseAnnotation
                    {
                        Bounds = ellipseBounds,
                        StrokeColor = freehand.StrokeColor,
                        Thickness = freehand.Thickness
                    };
                }

                return new RectAnnotation
                {
                    Bounds = bounds,
                    StrokeColor = freehand.StrokeColor,
                    Thickness = freehand.Thickness
                };
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
            if (_selected == null) return;
            PushUndo();
            _selected.StrokeColor = color;
            InvalidateVisual();
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetSelectedThickness(double thickness)
        {
            if (_selected == null || _selected is TextAnnotation || _selected is MosaicAnnotation) return;
            PushUndo();
            _selected.Thickness = thickness;
            InvalidateVisual();
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void AdjustSelectedFontSize(double delta)
        {
            if (!(_selected is TextAnnotation text)) return;
            PushUndo();
            text.FontSize = Math.Max(12, Math.Min(160, text.FontSize + delta));
            InvalidateVisual();
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetSelectedFontSize(double fontSize)
        {
            if (!(_selected is TextAnnotation text)) return;
            PushUndo();
            text.FontSize = Math.Max(12, Math.Min(160, fontSize));
            InvalidateVisual();
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void EditSelectedText(string newText, double fontSize)
        {
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
            _undo.Clear();
            InvalidateVisual();
        }

        public void SetWatermark(WatermarkSettings settings, bool notifyChanged = true)
        {
            _watermark = settings?.Clone();
            InvalidateVisual();
            WatermarkChanged?.Invoke(this, EventArgs.Empty);
            if (notifyChanged)
                AnnotationsChanged?.Invoke(this, EventArgs.Empty);
        }

        public WatermarkSettings GetWatermark() => _watermark?.Clone();

        private void PushUndo() => _undo.PushState(Annotations);

        public void Undo()
        {
            var state = _undo.Undo(Annotations);
            if (state != null) ReplaceAnnotations(state);
        }

        public void Redo()
        {
            var state = _undo.Redo(Annotations);
            if (state != null) ReplaceAnnotations(state);
        }

        private void ReplaceAnnotations(List<Annotation> state)
        {
            Annotations.Clear();
            Annotations.AddRange(state);
            RemoveTextEditor();
            _editingText = null;
            SetSelected(null);
            InvalidateVisual();
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
