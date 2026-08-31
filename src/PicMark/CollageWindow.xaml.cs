using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PicMark
{
    public partial class CollageWindow : Window
    {
        private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };
        private const int MaximumImages = 12;
        private const int MaximumOutputDimension = 8000;

        private readonly List<CollageItem> _items = new List<CollageItem>();
        private readonly string _initialPath;
        private bool _syncingSelection;
        private bool _busy;
        private string _ratioTag = "Auto";

        private int ImageCount => _items.Count(HasImage);

        public CollageWindow()
            : this(null)
        {
        }

        public CollageWindow(string initialPath)
        {
            InitializeComponent();
            _initialPath = initialPath;
            CollagePreview.Items = _items;
            CollagePreview.Template = CollageTemplateKind.FourGrid;
            CollagePreview.CanvasBackground = Brushes.White;
            Loaded += CollageWindow_Loaded;
        }

        private async void CollageWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateTemplateUi("四宫格");
            UpdatePreviewSize();
            if (IsSupportedImagePath(_initialPath))
                await AddPathsAsync(new[] { _initialPath });
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else
                DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private async void AddImages_Click(object sender, RoutedEventArgs e)
        {
            await ChooseImagesAsync(null);
        }

        private async Task ChooseImagesAsync(int? targetSlot)
        {
            if (_busy) return;
            var dialog = new OpenFileDialog
            {
                Multiselect = !targetSlot.HasValue,
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.webp",
                Title = targetSlot.HasValue ? $"为第 {targetSlot.Value + 1} 格选择图片" : "选择拼图图片"
            };
            string initialDirectory = GetInitialDirectory();
            if (!string.IsNullOrWhiteSpace(initialDirectory)) dialog.InitialDirectory = initialDirectory;
            if (dialog.ShowDialog(this) == true)
                await AddPathsAsync(dialog.FileNames, targetSlot);
        }

        private async Task AddPathsAsync(IEnumerable<string> paths, int? targetSlot = null)
        {
            int remainingCapacity = Math.Max(0, MaximumImages - ImageCount);
            var candidates = (paths ?? Enumerable.Empty<string>())
                .Where(IsSupportedImagePath)
                .Where(path => !_items.Any(item => HasImage(item) && string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(remainingCapacity)
                .ToList();
            if (candidates.Count == 0)
            {
                if (ImageCount >= MaximumImages)
                    AppDialog.Show(this, $"为保证低配电脑流畅，一次拼图最多添加 {MaximumImages} 张图片。", "图片数量已满");
                return;
            }

            SetBusy(true, "正在读取图片...");
            var failed = new List<string>();
            int firstAddedSlot = -1;
            int nextTargetSlot = targetSlot ?? 0;
            try
            {
                foreach (string path in candidates)
                {
                    try
                    {
                        BitmapSource image = await Task.Run(() => LoadBitmap(path, 1200));
                        int slot = FindNextEmptySlot(targetSlot.HasValue ? nextTargetSlot : 0);
                        if (slot >= MaximumImages) break;
                        SetItemAtSlot(slot, new CollageItem { Path = path, Image = image });
                        if (firstAddedSlot < 0) firstAddedSlot = slot;
                        nextTargetSlot = slot + 1;
                    }
                    catch
                    {
                        failed.Add(Path.GetFileName(path));
                    }
                }
            }
            finally
            {
                SetBusy(false, string.Empty);
            }

            RefreshItems();
            if (firstAddedSlot >= 0)
                ImageListBox.SelectedIndex = firstAddedSlot;
            else if (ImageCount > 0 && ImageListBox.SelectedIndex < 0)
                ImageListBox.SelectedIndex = _items.FindIndex(HasImage);
            UpdatePreviewSize();
            UpdateStatus();
            if (failed.Count > 0)
                AppDialog.Show(this, "以下图片无法读取：\n" + string.Join("\n", failed.Take(6)), "部分图片未添加");
        }

        private int FindNextEmptySlot(int startIndex)
        {
            int start = Math.Max(0, startIndex);
            for (int i = start; i < _items.Count; i++)
                if (!HasImage(_items[i])) return i;
            return _items.Count;
        }

        private void SetItemAtSlot(int slot, CollageItem item)
        {
            while (_items.Count <= slot && _items.Count < MaximumImages)
                _items.Add(new CollageItem());
            if (slot >= 0 && slot < _items.Count)
                _items[slot] = item;
        }

        private static bool HasImage(CollageItem item) => item != null && item.Image != null;

        private async void CollagePreview_EmptySlotClicked(object sender, CollageSlotEventArgs e)
        {
            await ChooseImagesAsync(e.SlotIndex);
        }

        private async void CollagePreview_EmptySlotDropped(object sender, CollageSlotDropEventArgs e)
        {
            if (_busy) return;
            await AddPathsAsync(CollectDroppedPaths(e.Paths), e.SlotIndex);
        }

        private static BitmapSource LoadBitmap(string path, int decodePixelWidth)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".webp")
                return WebpDecoder.Load(path);

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                if (decodePixelWidth > 0) bitmap.DecodePixelWidth = decodePixelWidth;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
        }

        private void RefreshItems()
        {
            int selected = ImageListBox.SelectedIndex;
            ImageListBox.ItemsSource = null;
            ImageListBox.ItemsSource = _items;
            ImageListBox.SelectedIndex = Math.Min(selected, _items.Count - 1);
            CollagePreview.Items = _items;
            CollagePreview.InvalidateVisual();
            UpdateStatus();
        }

        private void Template_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is string tag)) return;
            if (!Enum.TryParse(tag, out CollageTemplateKind template)) return;
            CollagePreview.Template = template;
            if (CollageCanvas.IsFlowTemplate(template) && _items.RemoveAll(item => !HasImage(item)) > 0)
                RefreshItems();
            string title = button.Content?.ToString() ?? "拼图";
            UpdateTemplateUi(title);
            UpdatePreviewSize();
            UpdateStatus();
        }

        private void UpdateTemplateUi(string title)
        {
            TemplateTitleText.Text = title;
            foreach (Button button in FindVisualChildren<Button>(this).Where(button => button.Tag is string value && Enum.TryParse(value, out CollageTemplateKind unused)))
            {
                bool selected = string.Equals(button.Tag as string, CollagePreview.Template.ToString(), StringComparison.Ordinal);
                button.Background = selected ? new SolidColorBrush(Color.FromRgb(0x20, 0x6D, 0x9B)) : new SolidColorBrush(Color.FromRgb(0x38, 0x3B, 0x40));
                button.BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(0x2F, 0xA8, 0xFF)) : new SolidColorBrush(Color.FromRgb(0x50, 0x54, 0x5A));
            }
            bool seamless = CollagePreview.Template == CollageTemplateKind.SeamlessVertical;
            bool flow = CollageCanvas.IsFlowTemplate(CollagePreview.Template);
            GapSlider.IsEnabled = !seamless;
            CanvasRatioCombo.IsEnabled = !flow;
            CollagePreview.Gap = seamless ? 0 : GapSlider.Value;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) yield break;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed) yield return typed;
                foreach (T descendant in FindVisualChildren<T>(child)) yield return descendant;
            }
        }

        private void ImageListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingSelection) return;
            _syncingSelection = true;
            CollagePreview.SelectedIndex = ImageListBox.SelectedIndex;
            _syncingSelection = false;
        }

        private void CollagePreview_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_syncingSelection) return;
            _syncingSelection = true;
            ImageListBox.SelectedIndex = CollagePreview.SelectedIndex;
            if (ImageListBox.SelectedItem != null) ImageListBox.ScrollIntoView(ImageListBox.SelectedItem);
            _syncingSelection = false;
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            int index = ImageListBox.SelectedIndex;
            if (index <= 0) return;
            CollageItem item = _items[index];
            _items.RemoveAt(index);
            _items.Insert(index - 1, item);
            RefreshItems();
            ImageListBox.SelectedIndex = index - 1;
            UpdatePreviewSize();
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            int index = ImageListBox.SelectedIndex;
            if (index < 0 || index >= _items.Count - 1) return;
            CollageItem item = _items[index];
            _items.RemoveAt(index);
            _items.Insert(index + 1, item);
            RefreshItems();
            ImageListBox.SelectedIndex = index + 1;
            UpdatePreviewSize();
        }

        private void RemoveImage_Click(object sender, RoutedEventArgs e)
        {
            int index = ImageListBox.SelectedIndex;
            if (index < 0 || index >= _items.Count) return;
            if (CollageCanvas.IsFlowTemplate(CollagePreview.Template))
                _items.RemoveAt(index);
            else
                _items[index] = new CollageItem();
            RefreshItems();
            ImageListBox.SelectedIndex = Math.Min(index, _items.Count - 1);
            UpdatePreviewSize();
        }

        private void ResetSelected_Click(object sender, RoutedEventArgs e) => CollagePreview.ResetSelectedImage();

        private void CanvasRatio_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CanvasRatioCombo?.SelectedItem is ComboBoxItem item)
                _ratioTag = item.Tag as string ?? "Auto";
            UpdatePreviewSize();
        }

        private void GapSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (GapValueText == null || CollagePreview == null) return;
            GapValueText.Text = ((int)Math.Round(e.NewValue)).ToString();
            CollagePreview.Gap = CollagePreview.Template == CollageTemplateKind.SeamlessVertical ? 0 : e.NewValue;
        }

        private void Background_Click(object sender, RoutedEventArgs e)
        {
            string tag = (sender as Button)?.Tag as string;
            switch (tag)
            {
                case "Black": CollagePreview.CanvasBackground = Brushes.Black; break;
                case "Gray": CollagePreview.CanvasBackground = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8)); break;
                default: CollagePreview.CanvasBackground = Brushes.White; break;
            }
        }

        private void UpdatePreviewSize()
        {
            if (CollagePreview == null) return;
            double aspect = GetSelectedAspect();
            aspect = Math.Max(0.12, Math.Min(6, aspect));
            const double baseWidth = 800;
            double width = baseWidth;
            double height = width / aspect;
            if (height > 1200)
            {
                height = 1200;
                width = height * aspect;
            }
            CollagePreview.Width = Math.Max(120, width);
            CollagePreview.Height = Math.Max(120, height);
            CollagePreview.InvalidateVisual();
        }

        private double GetSelectedAspect()
        {
            if (CollageCanvas.IsFlowTemplate(CollagePreview.Template) || _ratioTag == "Auto")
                return CollageCanvas.NaturalAspect(CollagePreview.Template, _items);
            switch (_ratioTag)
            {
                case "1:1": return 1;
                case "4:3": return 4.0 / 3.0;
                case "16:9": return 16.0 / 9.0;
                case "A4": return 210.0 / 297.0;
                default: return 4.0 / 3.0;
            }
        }

        private void UpdateStatus()
        {
            if (StatusText == null || ImageCountHintText == null) return;
            int required = CollageCanvas.RequiredImageCount(CollagePreview.Template);
            bool flow = CollageCanvas.IsFlowTemplate(CollagePreview.Template);
            int imageCount = ImageCount;
            int usedImageCount = flow ? imageCount : _items.Take(required).Count(HasImage);
            ImageCountHintText.Text = flow ? $"已添加 {imageCount} 张" : $"需要 {required} 张 · 已添加 {usedImageCount} 张";
            if (_busy) return;
            if (imageCount == 0)
                StatusText.Text = "点击任意空格添加图片，也可以直接拖入空格";
            else if (!HasRequiredImages())
                StatusText.Text = $"还需要 {Math.Max(0, required - usedImageCount)} 张图片";
            else if (!flow && imageCount > required)
                StatusText.Text = $"当前模板使用前 {required} 张，可用上移/下移调整顺序";
            else
                StatusText.Text = "可以拖动图片、滚轮缩放或调整分隔线";
            ExportButton.IsEnabled = !_busy && HasRequiredImages();
        }

        private bool HasRequiredImages()
        {
            int required = CollageCanvas.RequiredImageCount(CollagePreview.Template);
            if (CollageCanvas.IsFlowTemplate(CollagePreview.Template))
                return ImageCount >= required;
            return _items.Count >= required && _items.Take(required).All(HasImage);
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            int required = CollageCanvas.RequiredImageCount(CollagePreview.Template);
            if (!HasRequiredImages())
            {
                AppDialog.Show(this, $"当前模板至少需要 {required} 张图片。", "图片不足");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "PNG 图片|*.png|JPEG 图片|*.jpg",
                DefaultExt = ".png",
                AddExtension = true,
                FileName = "拼图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png",
                Title = "导出拼图"
            };
            string initialDirectory = GetInitialDirectory();
            if (!string.IsNullOrWhiteSpace(initialDirectory)) dialog.InitialDirectory = initialDirectory;
            if (dialog.ShowDialog(this) != true) return;

            GetExportSize(out int width, out int height);
            if (width <= 0 || height <= 0 || width > MaximumOutputDimension || height > MaximumOutputDimension)
            {
                AppDialog.Show(this, "拼图尺寸过大，请减少图片数量或选择较小的导出宽度。", "无法导出");
                return;
            }

            bool flow = CollageCanvas.IsFlowTemplate(CollagePreview.Template);
            var sourceItems = flow ? _items.Where(HasImage).ToList() : _items.Take(required).ToList();
            SetBusy(true, $"正在生成 {width} × {height} 拼图...");
            try
            {
                int decodeWidth = Math.Min(3000, Math.Max(1200, width));
                var exportItems = new List<CollageItem>();
                foreach (CollageItem item in sourceItems)
                {
                    BitmapSource image = await Task.Run(() => LoadBitmap(item.Path, decodeWidth));
                    exportItems.Add(item.WithImage(image));
                }

                RenderTargetBitmap bitmap = CollagePreview.RenderBitmap(width, height, exportItems);
                BitmapEncoder encoder;
                string extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                if (extension == ".jpg" || extension == ".jpeg")
                    encoder = new JpegBitmapEncoder { QualityLevel = 92 };
                else
                    encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write, FileShare.None))
                    encoder.Save(stream);
                StatusText.Text = "已导出：" + Path.GetFileName(dialog.FileName);
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, "导出拼图失败：" + ex.Message, "导出失败");
            }
            finally
            {
                SetBusy(false, string.Empty);
            }
        }

        private void GetExportSize(out int width, out int height)
        {
            width = 1600;
            if (ExportWidthCombo.SelectedItem is ComboBoxItem widthItem)
                int.TryParse(widthItem.Tag as string, out width);
            if (width <= 0) width = 1600;
            if (!CollageCanvas.IsFlowTemplate(CollagePreview.Template) && _ratioTag == "A4")
            {
                width = 1654;
                height = 2339;
                return;
            }
            double aspect = Math.Max(0.05, GetSelectedAspect());
            height = Math.Max(1, (int)Math.Round(width / aspect));
            if (height > MaximumOutputDimension)
            {
                double scale = (double)MaximumOutputDimension / height;
                height = MaximumOutputDimension;
                width = Math.Max(1, (int)Math.Round(width * scale));
            }
        }

        private void SetBusy(bool busy, string message)
        {
            _busy = busy;
            AddImagesButton.IsEnabled = !busy;
            ExportButton.IsEnabled = !busy;
            if (!string.IsNullOrWhiteSpace(message)) StatusText.Text = message;
            if (!busy) UpdateStatus();
        }

        private string GetInitialDirectory()
        {
            string path = _items.FirstOrDefault(HasImage)?.Path ?? _initialPath;
            if (string.IsNullOrWhiteSpace(path)) return null;
            try { return Path.GetDirectoryName(path); }
            catch { return null; }
        }

        private static bool IsSupportedImagePath(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && File.Exists(path)
                && Array.IndexOf(SupportedExtensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (_busy || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            await AddPathsAsync(CollectDroppedPaths(e.Data));
        }

        private static List<string> CollectDroppedPaths(object data)
        {
            if (!(data is IDataObject dragData) || !dragData.GetDataPresent(DataFormats.FileDrop))
                return new List<string>();
            return CollectDroppedPaths(dragData.GetData(DataFormats.FileDrop) as string[]);
        }

        private static List<string> CollectDroppedPaths(IEnumerable<string> droppedPaths)
        {
            var paths = new List<string>();
            foreach (string path in droppedPaths ?? Enumerable.Empty<string>())
            {
                if (Directory.Exists(path))
                {
                    try
                    {
                        paths.AddRange(Directory.EnumerateFiles(path).Where(IsSupportedImagePath).Take(MaximumImages));
                    }
                    catch { }
                }
                else
                {
                    paths.Add(path);
                }
            }
            return paths;
        }
    }
}
