using System.Collections.Generic;
using System.Linq;

namespace PicMark
{
    public class UndoManager
    {
        private readonly Stack<List<Annotation>> _undoStack = new Stack<List<Annotation>>();
        private readonly Stack<List<Annotation>> _redoStack = new Stack<List<Annotation>>();

        public void PushState(List<Annotation> current)
        {
            _undoStack.Push(Clone(current));
            _redoStack.Clear();
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }

        public List<Annotation> Undo(List<Annotation> current)
        {
            if (_undoStack.Count == 0) return null;
            _redoStack.Push(Clone(current));
            return _undoStack.Pop();
        }

        public List<Annotation> Redo(List<Annotation> current)
        {
            if (_redoStack.Count == 0) return null;
            _undoStack.Push(Clone(current));
            return _redoStack.Pop();
        }

        private static List<Annotation> Clone(List<Annotation> list) => list.Select(a => a.Clone()).ToList();
    }
}
