using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;

namespace PicMark
{
    public class UndoManager
    {
        public class CanvasState
        {
            public BitmapSource Image;
            public List<Annotation> Annotations;
            public WatermarkSettings Watermark;
        }

        private readonly Stack<CanvasState> _undoStack = new Stack<CanvasState>();
        private readonly Stack<CanvasState> _redoStack = new Stack<CanvasState>();

        public void PushState(BitmapSource image, List<Annotation> current, WatermarkSettings watermark)
        {
            _undoStack.Push(new CanvasState { Image = image, Annotations = Clone(current), Watermark = watermark?.Clone() });
            _redoStack.Clear();
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }

        public CanvasState Undo(BitmapSource currentImage, List<Annotation> current, WatermarkSettings currentWatermark)
        {
            if (_undoStack.Count == 0) return null;
            _redoStack.Push(new CanvasState { Image = currentImage, Annotations = Clone(current), Watermark = currentWatermark?.Clone() });
            return _undoStack.Pop();
        }

        public CanvasState Redo(BitmapSource currentImage, List<Annotation> current, WatermarkSettings currentWatermark)
        {
            if (_redoStack.Count == 0) return null;
            _undoStack.Push(new CanvasState { Image = currentImage, Annotations = Clone(current), Watermark = currentWatermark?.Clone() });
            return _redoStack.Pop();
        }

        private static List<Annotation> Clone(List<Annotation> list) => list.Select(a => a.Clone()).ToList();
    }
}
