using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;
using Path = System.IO.Path;

namespace PicMark
{
    public partial class BatchCropWindow : Window
    {
        private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };
        private const double MinKeepFraction = 0.05;

        private enum DragEdge { None, Top, Bottom, Left, Right }

        private readonly List<BatchCropPreset> _presets;
        private readonly List<string> _filePaths = new List<string>();
        private readonly Dictionary<string, BitmapSource> _imageCache = new Dictionary<string, BitmapSource>();

        private BitmapSource _sampleImage;
        private double _marginTopPct, _marginBottomPct, _marginLeftPct, _marginRightPct;
        private DragEdge _dragEdge = DragEdge.None;

        public BatchCropWindow()
        {
            InitializeComponent();
            _presets = BatchCropPresetStore.Load();
            RefreshPresetCombo();
            RefreshFileList();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private BatchCropPreset SelectedPreset => PresetCombo.SelectedItem as BatchCropPreset;

        private void RefreshPresetCombo()
        {
            var current = SelectedPreset;
            PresetCombo.ItemsSource = null;
            PresetCombo.ItemsSource = _presets;
            if (current != null && _presets.Contains(current))
                PresetCombo.SelectedItem = current;
        }

        private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var preset = SelectedPreset;
            if (preset == null) return;
            _marginTopPct = preset.Top;
            _marginBottomPct = preset.Bottom;
            _marginLeftPct = preset.Left;
            _marginRightPct = preset.Right;
            RedrawMarginOverlay();
        }

        private void SavePreset_Click(object sender, RoutedEventArgs e)
        {
            if (_sampleImage == null)
            {
                AppDialog.Show(this, "请先添加文件并设置裁切边距，再保存预设。", "提示");
                return;
            }
            var nameDialog = new BatchCropPresetNameDialog(_sampleImage != null ? (double?)SampleAspect : null) { Owner = this };
            if (nameDialog.ShowDialog() != true) return;
            string name = nameDialog.ResultName;
            if (string.IsNullOrWhiteSpace(name)) return;

            var existing = _presets.FirstOrDefault(p => p.Name == name);
            if (existing == null)
            {
                existing = new BatchCropPreset { Name = name };
                _presets.Add(existing);
            }
            existing.Top = _marginTopPct;
            existing.Bottom = _marginBottomPct;
            existing.Left = _marginLeftPct;
            existing.Right = _marginRightPct;
            BatchCropPresetStore.Save(_presets);
            RefreshPresetCombo();
            PresetCombo.SelectedItem = existing;
            StatusText.Text = $"已保存预设：{name}";
        }

        private void DeletePreset_Click(object sender, RoutedEventArgs e)
        {
            var preset = SelectedPreset;
            if (preset == null) return;
            _presets.Remove(preset);
            BatchCropPresetStore.Save(_presets);
            RefreshPresetCombo();
        }

        private void AddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.webp"
            };
            if (dlg.ShowDialog(this) != true) return;
            bool hadFiles = _filePaths.Count > 0;
            foreach (var path in dlg.FileNames)
                if (!_filePaths.Contains(path)) _filePaths.Add(path);
            RefreshFileList();
            if (!hadFiles && _filePaths.Count > 0) SetSample(_filePaths[0]);
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
                bool hadFiles = _filePaths.Count > 0;
                var found = Directory.EnumerateFiles(dlg.SelectedPath)
                    .Where(p => Array.IndexOf(SupportedExtensions, Path.GetExtension(p).ToLowerInvariant()) >= 0);
                foreach (var path in found)
                    if (!_filePaths.Contains(path)) _filePaths.Add(path);
                RefreshFileList();
                if (!hadFiles && _filePaths.Count > 0) SetSample(_filePaths[0]);
            }
        }

        private void RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileListBox.SelectedItems.Cast<string>().ToList();
            bool sampleRemoved = false;
            foreach (var path in selected)
            {
                _filePaths.Remove(path);
                _imageCache.Remove(path);
                if (Equals(path, FileListBox.Tag)) sampleRemoved = true;
            }
            RefreshFileList();
            if (sampleRemoved || _sampleImage == null)
            {
                if (_filePaths.Count > 0) SetSample(_filePaths[0]);
                else
                {
                    _sampleImage = null;
                    MarginCanvas.Children.Clear();
                    PreviewEmptyText.Visibility = Visibility.Visible;
                }
            }
        }

        private void RefreshFileList()
        {
            FileListBox.ItemsSource = null;
            FileListBox.ItemsSource = _filePaths;
            FileCountText.Text = _filePaths.Count > 0 ? $"已添加 {_filePaths.Count} 张图片" : "尚未添加图片";
        }

        private void FileListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FileListBox.SelectedItem is string path)
                SetSample(path);
        }

        private void SetSample(string path)
        {
            BitmapSource bmp;
            try
            {
                bmp = GetOrLoadImage(path);
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, $"无法打开图片：{ex.Message}", "提示");
                return;
            }

            FileListBox.Tag = path;
            _sampleImage = bmp;
            PreviewEmptyText.Visibility = Visibility.Collapsed;
            RedrawMarginOverlay();
        }

        private BitmapSource GetOrLoadImage(string path)
        {
            if (_imageCache.TryGetValue(path, out var cached)) return cached;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            BitmapSource bmp;
            if (ext == ".webp")
            {
                bmp = WebpDecoder.Load(path);
            }
            else
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = fs;
                    bi.EndInit();
                    bi.Freeze();
                    bmp = bi;
                }
            }
            _imageCache[path] = bmp;
            return bmp;
        }

        private double SampleAspect => _sampleImage != null ? (double)_sampleImage.PixelWidth / _sampleImage.PixelHeight : 1;

        private void GetCanvasSize(out double w, out double h)
        {
            const double maxSize = 380;
            double aspect = SampleAspect;
            if (aspect >= 1) { w = maxSize; h = maxSize / aspect; }
            else { h = maxSize; w = maxSize * aspect; }
        }

        private void RedrawMarginOverlay()
        {
            MarginCanvas.Children.Clear();
            if (_sampleImage == null) return;

            GetCanvasSize(out double w, out double h);
            MarginCanvas.Width = w;
            MarginCanvas.Height = h;

            MarginCanvas.Children.Add(new System.Windows.Controls.Image
            {
                Source = _sampleImage,
                Width = w,
                Height = h,
                Stretch = Stretch.Fill
            });

            double topY = _marginTopPct * h;
            double bottomY = h - _marginBottomPct * h;
            double leftX = _marginLeftPct * w;
            double rightX = w - _marginRightPct * w;

            var mask = Color.FromArgb(150, 0, 0, 0);
            AddMask(0, 0, w, topY, mask);
            AddMask(0, bottomY, w, h - bottomY, mask);
            AddMask(0, topY, leftX, bottomY - topY, mask);
            AddMask(rightX, topY, w - rightX, bottomY - topY, mask);

            var border = new Rectangle
            {
                Width = Math.Max(0, rightX - leftX),
                Height = Math.Max(0, bottomY - topY),
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(border, leftX);
            Canvas.SetTop(border, topY);
            MarginCanvas.Children.Add(border);

            AddHandle((leftX + rightX) / 2, topY);
            AddHandle((leftX + rightX) / 2, bottomY);
            AddHandle(leftX, (topY + bottomY) / 2);
            AddHandle(rightX, (topY + bottomY) / 2);

            UpdateMarginInfoText();
        }

        private void AddMask(double x, double y, double width, double height, Color color)
        {
            if (width <= 0 || height <= 0) return;
            var rect = new Rectangle { Width = width, Height = height, Fill = new SolidColorBrush(color) };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            MarginCanvas.Children.Add(rect);
        }

        private void AddHandle(double cx, double cy)
        {
            const double size = 9;
            var rect = new Rectangle { Width = size, Height = size, Fill = Brushes.White, Stroke = Brushes.White, StrokeThickness = 1 };
            Canvas.SetLeft(rect, cx - size / 2);
            Canvas.SetTop(rect, cy - size / 2);
            MarginCanvas.Children.Add(rect);
        }

        private void UpdateMarginInfoText()
        {
            MarginInfoText.Text = $"上 {_marginTopPct:P0}  下 {_marginBottomPct:P0}  左 {_marginLeftPct:P0}  右 {_marginRightPct:P0}";
        }

        private void MarginCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_sampleImage == null) return;
            var p = e.GetPosition(MarginCanvas);
            double w = MarginCanvas.Width;
            double h = MarginCanvas.Height;
            double topY = _marginTopPct * h;
            double bottomY = h - _marginBottomPct * h;
            double leftX = _marginLeftPct * w;
            double rightX = w - _marginRightPct * w;
            const double tol = 8;

            if (Math.Abs(p.Y - topY) <= tol) _dragEdge = DragEdge.Top;
            else if (Math.Abs(p.Y - bottomY) <= tol) _dragEdge = DragEdge.Bottom;
            else if (Math.Abs(p.X - leftX) <= tol) _dragEdge = DragEdge.Left;
            else if (Math.Abs(p.X - rightX) <= tol) _dragEdge = DragEdge.Right;
            else _dragEdge = DragEdge.None;

            if (_dragEdge != DragEdge.None) MarginCanvas.CaptureMouse();
        }

        private void MarginCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_sampleImage == null) return;
            var p = e.GetPosition(MarginCanvas);
            double w = MarginCanvas.Width;
            double h = MarginCanvas.Height;

            if (_dragEdge == DragEdge.None)
            {
                double topY0 = _marginTopPct * h;
                double bottomY0 = h - _marginBottomPct * h;
                double leftX0 = _marginLeftPct * w;
                double rightX0 = w - _marginRightPct * w;
                const double tol = 8;
                if (Math.Abs(p.Y - topY0) <= tol || Math.Abs(p.Y - bottomY0) <= tol)
                    MarginCanvas.Cursor = Cursors.SizeNS;
                else if (Math.Abs(p.X - leftX0) <= tol || Math.Abs(p.X - rightX0) <= tol)
                    MarginCanvas.Cursor = Cursors.SizeWE;
                else
                    MarginCanvas.Cursor = Cursors.Arrow;
                return;
            }

            switch (_dragEdge)
            {
                case DragEdge.Top:
                    _marginTopPct = Clamp(p.Y / h, 0, 1 - _marginBottomPct - MinKeepFraction);
                    break;
                case DragEdge.Bottom:
                    _marginBottomPct = Clamp(1 - p.Y / h, 0, 1 - _marginTopPct - MinKeepFraction);
                    break;
                case DragEdge.Left:
                    _marginLeftPct = Clamp(p.X / w, 0, 1 - _marginRightPct - MinKeepFraction);
                    break;
                case DragEdge.Right:
                    _marginRightPct = Clamp(1 - p.X / w, 0, 1 - _marginLeftPct - MinKeepFraction);
                    break;
            }
            RedrawMarginOverlay();
        }

        private static double Clamp(double value, double min, double max) =>
            Math.Max(min, Math.Min(max, value));

        private void MarginCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragEdge == DragEdge.None) return;
            _dragEdge = DragEdge.None;
            MarginCanvas.ReleaseMouseCapture();
        }

        private async void Run_Click(object sender, RoutedEventArgs e)
        {
            if (_filePaths.Count == 0)
            {
                AppDialog.Show(this, "请先添加要批量裁切的文件。", "提示");
                return;
            }
            if (_marginTopPct <= 0 && _marginBottomPct <= 0 && _marginLeftPct <= 0 && _marginRightPct <= 0)
            {
                AppDialog.Show(this, "请先在样图上拖动边缘设置裁切范围。", "提示");
                return;
            }

            RunButton.IsEnabled = false;
            RunProgress.Maximum = _filePaths.Count;
            RunProgress.Value = 0;

            int succeeded = 0;
            var failed = new List<string>();
            double top = _marginTopPct, bottom = _marginBottomPct, left = _marginLeftPct, right = _marginRightPct;

            foreach (var path in _filePaths)
            {
                StatusText.Text = $"正在处理 {Path.GetFileName(path)}";
                try
                {
                    bool ok = await System.Threading.Tasks.Task.Run(() => CropOneFile(path, top, bottom, left, right));
                    if (ok) succeeded++; else failed.Add(Path.GetFileName(path));
                }
                catch
                {
                    failed.Add(Path.GetFileName(path));
                }
                RunProgress.Value++;
            }

            RunButton.IsEnabled = true;
            StatusText.Text = "完成";

            var summary = $"批量裁切完成：成功 {succeeded} 个，失败 {failed.Count} 个。结果保存在各文件所在目录的「已裁剪」子文件夹中。";
            if (failed.Count > 0)
                summary += "\n\n失败文件：\n" + string.Join("\n", failed.Take(20)) + (failed.Count > 20 ? $"\n还有 {failed.Count - 20} 个" : "");
            AppDialog.Show(this, summary, "批量裁切结果");
        }

        private bool CropOneFile(string path, double top, double bottom, double left, double right)
        {
            BitmapSource bmp;
            lock (_imageCache)
            {
                bmp = GetOrLoadImage(path);
            }

            int x = (int)Math.Round(left * bmp.PixelWidth);
            int y = (int)Math.Round(top * bmp.PixelHeight);
            int w = bmp.PixelWidth - x - (int)Math.Round(right * bmp.PixelWidth);
            int h = bmp.PixelHeight - y - (int)Math.Round(bottom * bmp.PixelHeight);
            if (w < 1 || h < 1) return false;

            var cropped = new CroppedBitmap(bmp, new Int32Rect(x, y, w, h));
            cropped.Freeze();

            string dir = Path.GetDirectoryName(path);
            string outDir = Path.Combine(dir ?? ".", "已裁剪");
            Directory.CreateDirectory(outDir);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string outExt = ext == ".webp" ? ".png" : ext;
            string outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + outExt);

            byte[] bytes = MainWindow.EncodeBitmap(cropped, outExt, 95);
            File.WriteAllBytes(outPath, bytes);
            return true;
        }

    }
}
