using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PicMark
{
    public partial class MainWindow : Window
    {
        private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };
        private static readonly string[] SaveableExtensions = { ".jpg", ".jpeg", ".png", ".bmp" };
        private const double MinimumWindowWidth = 1180;
        private const double MinimumWindowHeight = 720;

        private string _currentFilePath;
        private string _currentExtension = ".png";

        private readonly List<string> _batchFiles = new List<string>();
        private int _batchIndex = -1;
        private bool _hasUnsavedChanges;
        private readonly AppSettings _settings;
        private bool _updatingZoomCombo;

        public MainWindow()
        {
            InitializeComponent();
            _settings = AppSettings.Load();
            ApplyWindowSettings();
            Canvas1.AnnotationsChanged += (s, e) => MarkDirty();
            Canvas1.SelectionChanged += Canvas1_SelectionChanged;
            Canvas1.TextEditFinished += (s, e) => SetActiveTool("Select");
            Canvas1.ScaleChanged += (s, e) => UpdateZoomControls();
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            Closing += MainWindow_Closing;
            Scroller.PreviewMouseWheel += Scroller_PreviewMouseWheel;
            ApplyOptionSettings();
            HistoryCacheText.Text = _settings.HistoryCacheMb.ToString();
            UpdateHistoryCacheInfo();
            UpdateZoomControls();
        }

        public void OpenInitialFiles(IEnumerable<string> paths)
        {
            var files = paths?
                .Where(File.Exists)
                .Where(path => Array.IndexOf(SupportedExtensions, Path.GetExtension(path).ToLowerInvariant()) >= 0)
                .ToArray();

            if (files == null || files.Length == 0) return;
            if (files.Length == 1)
            {
                ClearBatch();
                LoadImage(files[0]);
            }
            else
            {
                StartBatch(files);
            }
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            if (ctrl && e.Key == Key.Z && !shift) { Canvas1.Undo(); e.Handled = true; }
            else if (ctrl && ((e.Key == Key.Z && shift) || e.Key == Key.Y)) { Canvas1.Redo(); e.Handled = true; }
            else if (ctrl && e.Key == Key.S) { BtnSave_Click(this, null); e.Handled = true; }
            else if (ctrl && e.Key == Key.O) { BtnOpen_Click(this, null); e.Handled = true; }
            else if (ctrl && e.Key == Key.V) { PasteFromClipboard(); e.Handled = true; }
            else if (ctrl && e.Key == Key.D) { Canvas1.Deselect(); e.Handled = true; }
            else if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add)) { Canvas1.Scale *= 1.25; e.Handled = true; }
            else if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract)) { Canvas1.Scale /= 1.25; e.Handled = true; }
            else if (ctrl && e.Key == Key.D0) { AutoFitZoom(); e.Handled = true; }
            else if (ctrl && e.Key == Key.D1) { Canvas1.Scale = 1.0; e.Handled = true; }
            else if ((e.Key == Key.Delete || e.Key == Key.Back) && Canvas1.HasSelection) { Canvas1.DeleteSelected(); e.Handled = true; }
        }

        private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Canvas1.Image == null) return;
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;

            Canvas1.Scale *= e.Delta > 0 ? 1.15 : 1 / 1.15;
            e.Handled = true;
        }

        private void ApplyWindowSettings()
        {
            MinWidth = MinimumWindowWidth;
            MinHeight = MinimumWindowHeight;
            Width = Math.Max(MinimumWindowWidth, _settings.WindowWidth);
            Height = Math.Max(MinimumWindowHeight, _settings.WindowHeight);
            if (!double.IsNaN(_settings.WindowLeft) && !double.IsNaN(_settings.WindowTop))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = _settings.WindowLeft;
                Top = _settings.WindowTop;
            }
            if (_settings.WindowState == WindowState.Maximized)
                WindowState = WindowState.Maximized;
        }

        private void ApplyOptionSettings()
        {
            string tool = Enum.TryParse(_settings.Tool, out AnnotationTool _) ? _settings.Tool : "Select";
            string color = string.IsNullOrWhiteSpace(_settings.Color) ? "Red" : _settings.Color;
            string thickness = new[] { "3", "6", "12" }.Contains(_settings.Thickness) ? _settings.Thickness : "6";
            string fontSize = new[] { "24", "36", "52", "72" }.Contains(_settings.FontSize) ? _settings.FontSize : "36";

            SetActiveTool(tool);
            SetColorOption(color);
            SetThicknessOption(thickness);
            SetFontSizeOption(fontSize);
        }

        private void SaveWindowSettings()
        {
            ApplyHistoryCacheSetting(false);
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            _settings.WindowLeft = bounds.Left;
            _settings.WindowTop = bounds.Top;
            _settings.WindowWidth = bounds.Width;
            _settings.WindowHeight = bounds.Height;
            _settings.WindowState = WindowState;
            _settings.Save();
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (!ConfirmDiscardUnsavedChanges("关闭程序")) e.Cancel = true;
            if (!e.Cancel) SaveWindowSettings();
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length == 0) return;

            if (!ConfirmDiscardUnsavedChanges("打开新图片")) return;

            if (files.Length > 1)
            {
                StartBatch(files);
            }
            else
            {
                ClearBatch();
                LoadImage(files[0]);
            }
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.webp|所有文件|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                if (!ConfirmDiscardUnsavedChanges("打开新图片")) return;
                ClearBatch();
                LoadImage(dlg.FileName);
            }
        }

        private void BtnOpenMultiple_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.webp|所有文件|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
            {
                if (!ConfirmDiscardUnsavedChanges("打开新图片")) return;
                if (dlg.FileNames.Length == 1)
                {
                    ClearBatch();
                    LoadImage(dlg.FileNames[0]);
                }
                else
                {
                    StartBatch(dlg.FileNames);
                }
            }
        }

        private void StartBatch(string[] files)
        {
            _batchFiles.Clear();
            _batchFiles.AddRange(files);
            _batchIndex = 0;
            LoadImage(_batchFiles[_batchIndex]);
            UpdateBatchBar();
        }

        private void ClearBatch()
        {
            _batchFiles.Clear();
            _batchIndex = -1;
            UpdateBatchBar();
        }

        private void BtnPrevImage_Click(object sender, RoutedEventArgs e) => GoToBatchIndex(_batchIndex - 1);
        private void BtnNextImage_Click(object sender, RoutedEventArgs e) => GoToBatchIndex(_batchIndex + 1);

        private void GoToBatchIndex(int newIndex)
        {
            if (newIndex < 0 || newIndex >= _batchFiles.Count) return;
            if (newIndex == _batchIndex) return;

            if (_hasUnsavedChanges)
            {
                if (!ConfirmDiscardUnsavedChanges("切换图片")) return;
            }

            _batchIndex = newIndex;
            LoadImage(_batchFiles[_batchIndex]);
            UpdateBatchBar();
        }

        private void UpdateBatchBar()
        {
            if (_batchFiles.Count > 1)
            {
                BatchBar.Visibility = Visibility.Visible;
                BatchPositionText.Text = $"第 {_batchIndex + 1} 张，共 {_batchFiles.Count} 张：{Path.GetFileName(_batchFiles[_batchIndex])}";
            }
            else
            {
                BatchBar.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadImage(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (Array.IndexOf(SupportedExtensions, ext) < 0)
            {
                MessageBox.Show(this, "暂不支持这种图片格式，请使用 jpg、png、bmp 或 webp 格式。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BitmapSource bmp;
            try
            {
                bmp = ext == ".webp" ? WebpDecoder.Load(path) : LoadStandardBitmap(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"无法打开这张图片：{ex.Message}", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Canvas1.Image = bmp;
            Canvas1.ClearAll();
            _currentFilePath = path;
            _currentExtension = ext == ".webp" ? ".png" : ext;
            _hasUnsavedChanges = false;
            TopFileNameText.Text = Path.GetFileName(path);
            FitImageAfterLayout();
            UpdateStatus($"已打开：{Path.GetFileName(path)}（{bmp.PixelWidth}×{bmp.PixelHeight}像素）");
        }

        private static BitmapSource LoadStandardBitmap(string path)
        {
            BitmapImage bmp;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = fs;
                bmp.EndInit();
            }
            bmp.Freeze();
            return bmp;
        }

        private void PasteFromClipboard()
        {
            if (!Clipboard.ContainsImage())
            {
                UpdateStatus("剪贴板里没有图片");
                return;
            }
            var src = Clipboard.GetImage();
            if (src == null) return;
            if (!ConfirmDiscardUnsavedChanges("粘贴新图片")) return;
            if (src.CanFreeze) src.Freeze();

            ClearBatch();
            Canvas1.Image = src;
            Canvas1.ClearAll();
            _currentFilePath = null;
            _currentExtension = ".png";
            _hasUnsavedChanges = false;
            TopFileNameText.Text = "剪贴板图片.png";
            FitImageAfterLayout();
            UpdateStatus("已粘贴剪贴板图片，保存时请选择保存位置");
        }

        private void FitImageAfterLayout()
        {
            Dispatcher.BeginInvoke((Action)AutoFitZoom, DispatcherPriority.Loaded);
        }

        private void AutoFitZoom()
        {
            if (Canvas1.Image == null) return;
            double viewW = Math.Max(Scroller.ActualWidth - 20, 200);
            double viewH = Math.Max(Scroller.ActualHeight - 20, 200);
            double fit = Math.Min(viewW / Canvas1.Image.PixelWidth, viewH / Canvas1.Image.PixelHeight);
            Canvas1.Scale = fit;
        }

        private void UpdateZoomControls()
        {
            if (ZoomCombo == null) return;
            _updatingZoomCombo = true;
            ZoomCombo.Text = $"{Math.Round(Canvas1.Scale * 100)}%";
            _updatingZoomCombo = false;
        }

        private void ZoomCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingZoomCombo || ZoomCombo.SelectedItem == null) return;
            var item = ZoomCombo.SelectedItem as ComboBoxItem;
            string tag = item?.Tag as string;
            if (tag == "Fit")
            {
                AutoFitZoom();
            }
            else if (double.TryParse(tag, out double scale))
            {
                Canvas1.Scale = scale;
            }
            ZoomCombo.SelectedItem = null;
            UpdateZoomControls();
        }

        private void BtnZoomFit_Click(object sender, RoutedEventArgs e) => AutoFitZoom();

        private void BtnPan_Click(object sender, RoutedEventArgs e)
        {
            SetActiveTool("Select");
            UpdateStatus("已切回选择/移动。");
        }

        private void ToolButton_Click(object sender, RoutedEventArgs e)
        {
            var tag = (string)((Button)sender).Tag;
            SetActiveTool(tag);
            if (tag == "Text")
                UpdateStatus("点击图片上的位置，然后直接输入文字；按 Enter 完成。");
        }

        private void SetActiveTool(string tag)
        {
            Canvas1.CurrentTool = (AnnotationTool)Enum.Parse(typeof(AnnotationTool), tag);
            SetToolButtonState(new[] { BtnSelect, BtnRect, BtnEllipse, BtnArrow, BtnFreehand, BtnMosaic, BtnText }, tag, Brushes.Transparent);
            SetToolButtonState(new[] { PanelBtnRect, PanelBtnEllipse, PanelBtnArrow, PanelBtnFreehand, PanelBtnMosaic, PanelBtnText }, tag, new SolidColorBrush(Color.FromRgb(0x42, 0x42, 0x42)));
            _settings.Tool = tag;
        }

        private void Color_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            Color selectedColor = SetColorOption((string)btn.Tag);
            if (Canvas1.HasSelection) Canvas1.SetSelectedColor(selectedColor);
        }

        private Color SetColorOption(string tag)
        {
            Color selectedColor;
            try
            {
                selectedColor = (Color)ColorConverter.ConvertFromString(tag);
            }
            catch
            {
                tag = "Red";
                selectedColor = Colors.Red;
            }
            Canvas1.CurrentColor = selectedColor;
            SetActiveColorButton(tag);
            _settings.Color = tag;
            return selectedColor;
        }

        private void SetActiveColorButton(string tag)
        {
            foreach (var btn in new[] { ClrRed, ClrGold, ClrLime, ClrSky, ClrBlack, ClrWhite, ClrRedPanel, ClrGoldPanel, ClrLimePanel, ClrSkyPanel, ClrBlackPanel, ClrWhitePanel })
                btn.BorderThickness = (string)btn.Tag == tag ? new Thickness(3) : new Thickness(1);
        }

        private void Thickness_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            double thickness = SetThicknessOption((string)btn.Tag);
            if (Canvas1.HasSelection) Canvas1.SetSelectedThickness(thickness);
        }

        private double SetThicknessOption(string tag)
        {
            double thickness = double.Parse(tag);
            Canvas1.CurrentThickness = thickness;
            SetActiveThicknessButton(tag);
            _settings.Thickness = tag;
            return thickness;
        }

        private void BtnFontSmaller_Click(object sender, RoutedEventArgs e)
        {
            Canvas1.CurrentFontSize = Math.Max(12, Canvas1.CurrentFontSize - 6);
            _settings.FontSize = ((int)Canvas1.CurrentFontSize).ToString();
            if (Canvas1.IsTextSelected) Canvas1.AdjustSelectedFontSize(-6);
        }

        private void BtnFontBigger_Click(object sender, RoutedEventArgs e)
        {
            Canvas1.CurrentFontSize = Math.Min(160, Canvas1.CurrentFontSize + 6);
            _settings.FontSize = ((int)Canvas1.CurrentFontSize).ToString();
            if (Canvas1.IsTextSelected) Canvas1.AdjustSelectedFontSize(6);
        }

        private void SetActiveThicknessButton(string tag)
        {
            foreach (var btn in new[] { ThinBtn, MidBtn, ThickBtn, ThinPanelBtn, MidPanelBtn, ThickPanelBtn })
                SetChoiceButtonState(btn, (string)btn.Tag == tag);
        }

        private void FontSize_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            double fontSize = SetFontSizeOption((string)btn.Tag);
            if (Canvas1.IsTextSelected) Canvas1.SetSelectedFontSize(fontSize);
        }

        private double SetFontSizeOption(string tag)
        {
            double fontSize = double.Parse(tag);
            Canvas1.CurrentFontSize = fontSize;
            SetActiveFontButton(tag);
            _settings.FontSize = tag;
            return fontSize;
        }

        private void SetToolButtonState(IEnumerable<Button> buttons, string tag, Brush normalBackground)
        {
            foreach (var btn in buttons)
            {
                bool active = (string)btn.Tag == tag;
                btn.Background = active ? (Brush)FindResource("ActiveBrush") : normalBackground;
                btn.Foreground = active ? Brushes.White : (Brush)FindResource("TextPrimaryBrush");
                btn.BorderBrush = active ? (Brush)FindResource("ActiveBorderBrush") : (Brush)FindResource("BorderBrush1");
                btn.BorderThickness = active ? new Thickness(2) : new Thickness(1);
            }
        }

        private void SetActiveFontButton(string tag)
        {
            foreach (var btn in new[] { FontSmallBtn, FontMidBtn, FontLargeBtn, FontHugeBtn, FontSmallPanelBtn, FontMidPanelBtn, FontLargePanelBtn, FontHugePanelBtn })
                SetChoiceButtonState(btn, (string)btn.Tag == tag);
        }

        private void SetChoiceButtonState(Button btn, bool active)
        {
            btn.Background = active ? (Brush)FindResource("ActiveBrush") : Brushes.Transparent;
            btn.Foreground = active ? Brushes.White : (Brush)FindResource("TextPrimaryBrush");
            btn.BorderBrush = active ? (Brush)FindResource("ActiveBorderBrush") : (Brush)FindResource("BorderBrush1");
            btn.BorderThickness = active ? new Thickness(2) : new Thickness(1);
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e) => Canvas1.Undo();
        private void BtnRedo_Click(object sender, RoutedEventArgs e) => Canvas1.Redo();
        private void BtnDelete_Click(object sender, RoutedEventArgs e) => Canvas1.DeleteSelected();
        private void BtnZoomIn_Click(object sender, RoutedEventArgs e) => Canvas1.Scale *= 1.25;
        private void BtnZoomOut_Click(object sender, RoutedEventArgs e) => Canvas1.Scale /= 1.25;
        private void BtnZoomReset_Click(object sender, RoutedEventArgs e) => Canvas1.Scale = 1.0;

        private void Canvas1_SelectionChanged(object sender, EventArgs e)
        {
            if (Canvas1.Selected is TextAnnotation text)
            {
                Canvas1.CurrentFontSize = text.FontSize;
                SetActiveFontButton(((int)text.FontSize).ToString());
                UpdateStatus("已选中文字。可直接拖动，双击编辑内容和字号。");
            }
            else if (Canvas1.HasSelection)
            {
                UpdateStatus("已选中标注。可拖动、改色、改粗细或按 Delete 删除。");
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e) => SaveAnnotatedImage(false);

        private void BtnSaveMenu_Click(object sender, RoutedEventArgs e)
        {
            SavePopup.IsOpen = true;
        }

        private void MenuSaveAs_Click(object sender, RoutedEventArgs e)
        {
            SavePopup.IsOpen = false;
            SaveAnnotatedImage(false);
        }

        private void MenuOverwrite_Click(object sender, RoutedEventArgs e)
        {
            SavePopup.IsOpen = false;
            BtnOverwrite_Click(sender, e);
        }

        private void MenuCopy_Click(object sender, RoutedEventArgs e)
        {
            SavePopup.IsOpen = false;
            if (Canvas1.Image == null)
            {
                MessageBox.Show(this, "请先打开一张图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Clipboard.SetImage(Canvas1.RenderFullResolution());
            UpdateStatus("已复制标注后的图片到剪贴板");
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnOverwrite_Click(object sender, RoutedEventArgs e)
        {
            if (_currentFilePath == null)
            {
                MessageBox.Show(this, "这张图片还没有对应的原文件路径（比如是粘贴来的截图），无法覆盖，请用“保存”另存为新文件。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var result = MessageBox.Show(this, "确定要覆盖原图吗？原图将被替换，无法恢复。",
                "确认覆盖原图", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes) SaveAnnotatedImage(true);
        }

        private bool SaveAnnotatedImage(bool overwrite)
        {
            if (Canvas1.Image == null)
            {
                MessageBox.Show(this, "请先打开一张图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            string targetPath;
            string ext = _currentExtension;
            if (Array.IndexOf(SaveableExtensions, ext) < 0) ext = ".png";

            if (overwrite && _currentFilePath != null)
            {
                targetPath = _currentFilePath;
            }
            else
            {
                string baseDir = _currentFilePath != null
                    ? Path.GetDirectoryName(_currentFilePath)
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                string baseName = _currentFilePath != null
                    ? Path.GetFileNameWithoutExtension(_currentFilePath) + "_标注"
                    : "粘贴图片_标注";
                string suggested = GetNonConflictingPath(baseDir, baseName, ext);

                var dlg = new SaveFileDialog
                {
                    Filter = "PNG 图片(完全无损)|*.png|JPEG 图片(高质量)|*.jpg|位图(无损)|*.bmp",
                    FileName = Path.GetFileName(suggested),
                    InitialDirectory = baseDir
                };
                if (dlg.ShowDialog() != true) return false;
                targetPath = dlg.FileName;
                ext = Path.GetExtension(targetPath).ToLowerInvariant();
            }

            var rtb = Canvas1.RenderFullResolution();
            BitmapEncoder encoder;
            if (ext == ".jpg" || ext == ".jpeg")
                encoder = new JpegBitmapEncoder { QualityLevel = 95 };
            else if (ext == ".bmp")
                encoder = new BmpBitmapEncoder();
            else
                encoder = new PngBitmapEncoder();

            encoder.Frames.Add(BitmapFrame.Create(rtb));

            using (var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
                encoder.Save(fs);

            _hasUnsavedChanges = false;
            UpdateStatus(overwrite ? $"已保存（已覆盖原图）：{targetPath}" : $"已保存：{targetPath}（原图未改动）");
            MessageBox.Show(this,
                overwrite ? "已保存好啦，原图已被覆盖。" : $"已保存好啦：\n{targetPath}\n原图没有被改动。",
                "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }

        private string CaptureHistoryVersion(string reason)
        {
            if (Canvas1.Image == null) return null;

            try
            {
                string path = HistoryManager.SaveVersion(
                    Canvas1.RenderFullResolution(),
                    _currentFilePath,
                    _settings.HistoryCacheMb,
                    reason);
                UpdateHistoryCacheInfo();
                return path;
            }
            catch (Exception ex)
            {
                UpdateStatus($"历史版本保存失败：{ex.Message}");
                return null;
            }
        }

        private void BtnHistory_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(HistoryManager.HistoryDirectory);
            Process.Start(HistoryManager.HistoryDirectory);
            UpdateHistoryCacheInfo();
        }

        private void BtnApplyHistoryCache_Click(object sender, RoutedEventArgs e)
        {
            ApplyHistoryCacheSetting(true);
            _settings.Save();
            UpdateHistoryCacheInfo();
        }

        private void ApplyHistoryCacheSetting(bool trimNow)
        {
            if (HistoryCacheText == null) return;
            if (!int.TryParse(HistoryCacheText.Text, out int cacheMb)) cacheMb = 500;
            cacheMb = Math.Max(20, Math.Min(cacheMb, 10240));
            _settings.HistoryCacheMb = cacheMb;
            HistoryCacheText.Text = cacheMb.ToString();
            if (trimNow) HistoryManager.TrimCache(cacheMb);
        }

        private void UpdateHistoryCacheInfo()
        {
            if (HistoryCacheInfoText == null) return;
            double usedMb = HistoryManager.GetCacheBytes() / 1024.0 / 1024.0;
            HistoryCacheInfoText.Text = $"当前约 {usedMb:0.#} MB / 上限 {_settings.HistoryCacheMb} MB";
        }

        private static string GetNonConflictingPath(string dir, string baseName, string ext)
        {
            string candidate = Path.Combine(dir, baseName + ext);
            int i = 2;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(dir, $"{baseName}({i}){ext}");
                i++;
            }
            return candidate;
        }

        private bool ConfirmDiscardUnsavedChanges(string actionName)
        {
            if (!_hasUnsavedChanges) return true;

            string historyPath = CaptureHistoryVersion(actionName);
            string historyLine = historyPath == null
                ? "尝试自动保存历史版本失败，请先手动另存为更稳妥。"
                : $"已自动存入历史版本：{Path.GetFileName(historyPath)}";

            var result = MessageBox.Show(this,
                $"当前图片还有未保存的标注。\n{historyLine}\n\n要另存为新图片再{actionName}吗？\n\n是：先另存为\n否：不另存，继续{actionName}\n取消：留在当前图片",
                "未保存的标注",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Cancel) return false;
            if (result == MessageBoxResult.No)
            {
                _hasUnsavedChanges = false;
                return true;
            }
            return SaveAnnotatedImage(false);
        }

        private void MarkDirty()
        {
            _hasUnsavedChanges = true;
            UpdateStatus("已修改，记得点击右上角“保存”");
        }

        private void UpdateStatus(string text) => StatusText.Text = text;
    }
}
