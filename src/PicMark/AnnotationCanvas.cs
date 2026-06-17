using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PicMark
{
    public class AnnotationCanvas : FrameworkElement
    {
        private BitmapSource _image;
        private double _scale = 1.0;
        private Annotation _selected;
        private Annotation _drawing;
        private Point _dragStartImagePoint;
        private bool _isMovingSelection;
        private bool _selectionMoved;
        private readonly UndoManager _undo = new UndoManager();

        public List<Annotation> Annotations { get; } = new List<Annotation>();
        public AnnotationTool CurrentTool { get; set; } = AnnotationTool.Select;
        public Color CurrentColor { get; set; } = Colors.Red;
        public double CurrentThickness { get; set; } = 6;
        public double CurrentFontSize { get; set; } = 36;

        public event EventHandler AnnotationsChanged;
        public event Action<Point> TextToolClicked;
        public event Action<TextAnnotation> TextAnnotationDoubleClicked;

        public Annotation Selected => _selected;
        public bool IsTextSelected => _selected is TextAnnotation;

        public BitmapSource Image
        {
            get => _image;
            set
            {
                _image = value;
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        public double Scale
        {
            get => _scale;
            set
            {
                _scale = Math.Max(0.05, Math.Min(value, 8.0));
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        public bool HasSelection => _selected != null;

        public AnnotationCanvas()
        {
            Focusable = true;
            ClipToBounds = true;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (Image == null) return new Size(0, 0);
            return new Size(Image.PixelWidth * Scale, Image.PixelHeight * Scale);
        }

        protected override Size ArrangeOverride(Size finalSize) => finalSize;

        protected override void OnRender(DrawingContext dc)
        {
            if (Image == null)
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)), null, new Rect(RenderSize));
                return;
            }

            dc.PushTransform(new ScaleTransform(Scale, Scale));
            dc.DrawImage(Image, new Rect(0, 0, Image.PixelWidth, Image.PixelHeight));
            foreach (var a in Annotations)
                a.Draw(dc, a == _selected, Image);
            _drawing?.Draw(dc, false, Image);
            dc.Pop();
        }

        private Point ToImagePoint(Point screenPoint) => new Point(screenPoint.X / Scale, screenPoint.Y / Scale);

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (Image == null) return;
            Focus();
            var p = ToImagePoint(e.GetPosition(this));

            switch (CurrentTool)
            {
                case AnnotationTool.Rectangle:
                    _dragStartImagePoint = p;
                    _drawing = new RectAnnotation { Bounds = new Rect(p, p), StrokeColor = CurrentColor, Thickness = CurrentThickness };
                    CaptureMouse();
                    break;
                case AnnotationTool.Ellipse:
                    _dragStartImagePoint = p;
                    _drawing = new EllipseAnnotation { Bounds = new Rect(p, p), StrokeColor = CurrentColor, Thickness = CurrentThickness };
                    CaptureMouse();
                    break;
                case AnnotationTool.Arrow:
                    _dragStartImagePoint = p;
                    _drawing = new ArrowAnnotation { Start = p, End = p, StrokeColor = CurrentColor, Thickness = CurrentThickness };
                    CaptureMouse();
                    break;
                case AnnotationTool.Freehand:
                    _drawing = new FreehandAnnotation { StrokeColor = CurrentColor, Thickness = CurrentThickness };
                    ((FreehandAnnotation)_drawing).Points.Add(p);
                    CaptureMouse();
                    break;
                case AnnotationTool.Mosaic:
                    _dragStartImagePoint = p;
                    _drawing = new MosaicAnnotation { Bounds = new Rect(p, p) };
                    CaptureMouse();
                    break;
                case AnnotationTool.Text:
                    TextToolClicked?.Invoke(p);
                    break;
                case AnnotationTool.Select:
                default:
                    var hit = HitTest(p);
                    if (e.ClickCount == 2 && hit is TextAnnotation textHit)
                    {
                        _selected = textHit;
                        InvalidateVisual();
                        TextAnnotationDoubleClicked?.Invoke(textHit);
                        break;
                    }
                    _selected = hit;
                    if (hit != null)
                    {
                        _isMovingSelection = true;
                        _selectionMoved = false;
                        _dragStartImagePoint = p;
                        CaptureMouse();
                    }
                    InvalidateVisual();
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
                el.Bounds = new Rect(_dragStartImagePoint, p);
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
                fh.Points.Add(p);
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
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (_drawing != null)
            {
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
                _isMovingSelection = false;
                bool moved = _selectionMoved;
                _selectionMoved = false;
                ReleaseMouseCapture();
                if (moved) AnnotationsChanged?.Invoke(this, EventArgs.Empty);
            }
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
            if (e.Key == Key.Delete)
            {
                DeleteSelected();
            }
            else if (e.Key == Key.Escape)
            {
                _selected = null;
                InvalidateVisual();
            }
        }

        public void DeleteSelected()
        {
            if (_selected == null) return;
            PushUndo();
            Annotations.Remove(_selected);
            _selected = null;
            InvalidateVisual();
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Deselect()
        {
            if (_selected == null) return;
            _selected = null;
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

        public void EditSelectedText(string newText)
        {
            if (!(_selected is TextAnnotation text) || string.IsNullOrWhiteSpace(newText)) return;
            PushUndo();
            text.Text = newText;
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

        public void AddTextAnnotation(Point imagePoint, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            PushUndo();
            Annotations.Add(new TextAnnotation
            {
                Location = imagePoint,
                Text = text,
                StrokeColor = CurrentColor,
                FontSize = CurrentFontSize
            });
            InvalidateVisual();
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearAll()
        {
            Annotations.Clear();
            _selected = null;
            _drawing = null;
            _isMovingSelection = false;
            _selectionMoved = false;
            _undo.Clear();
            InvalidateVisual();
        }

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
            _selected = null;
            InvalidateVisual();
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
        }

        public RenderTargetBitmap RenderFullResolution()
        {
            if (Image == null) return null;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(Image, new Rect(0, 0, Image.PixelWidth, Image.PixelHeight));
                foreach (var a in Annotations)
                    a.Draw(dc, false, Image);
            }
            var rtb = new RenderTargetBitmap(Image.PixelWidth, Image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            return rtb;
        }
    }
}
