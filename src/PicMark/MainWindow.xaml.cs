using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PicMark
{
    public partial class MainWindow : Window
    {
        private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };
        private static readonly string[] SaveableExtensions = { ".jpg", ".jpeg", ".png", ".bmp" };

        private string _currentFilePath;
        private string _currentExtension = ".png";

        private readonly List<string> _batchFiles = new List<string>();
        private int _batchIndex = -1;
        private bool _hasUnsavedChanges;

        public MainWindow()
        {
            InitializeComponent();
            Canvas1.TextToolClicked += Canvas1_TextToolClicked;
            Canvas1.TextAnnotationDoubleClicked += Canvas1_TextAnnotationDoubleClicked;
            Canvas1.AnnotationsChanged += (s, e) => MarkDirty();
            Canvas1.SelectionChanged += Canvas1_SelectionChanged;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            Closing += MainWindow_Closing;
            Scroller.PreviewMouseWheel += Scroller_PreviewMouseWheel;
            SetActiveTool("Select");
            SetActiveColorButton("Red");
            SetActiveThicknessButton("6");
            SetActiveFontButton("36");
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

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (!ConfirmDiscardUnsavedChanges("关闭程序")) e.Cancel = true;
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
            AutoFitZoom();
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
            AutoFitZoom();
            UpdateStatus("已粘贴剪贴板图片，保存时请选择保存位置");
        }

        private void AutoFitZoom()
        {
            if (Canvas1.Image == null) return;
            double viewW = Math.Max(Scroller.ActualWidth - 20, 200);
            double viewH = Math.Max(Scroller.ActualHeight - 20, 200);
            double fit = Math.Min(viewW / Canvas1.Image.PixelWidth, viewH / Canvas1.Image.PixelHeight);
            Canvas1.Scale = Math.Min(fit, 1.0);
        }

        private void ToolButton_Click(object sender, RoutedEventArgs e)
        {
            var tag = (string)((Button)sender).Tag;
            SetActiveTool(tag);
        }

        private void SetActiveTool(string tag)
        {
            Canvas1.CurrentTool = (AnnotationTool)Enum.Parse(typeof(AnnotationTool), tag);
            foreach (var btn in new[] { BtnSelect, BtnRect, BtnEllipse, BtnArrow, BtnFreehand, BtnMosaic, BtnText, PanelBtnRect, PanelBtnEllipse, PanelBtnArrow, PanelBtnFreehand, PanelBtnMosaic, PanelBtnText })
                btn.Background = (string)btn.Tag == tag ? new SolidColorBrush(Color.FromRgb(0xE6, 0xF4, 0xEA)) : Brushes.Transparent;
        }

        private void Color_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            var tag = (string)btn.Tag;
            var color = (Color)ColorConverter.ConvertFromString(tag);
            Canvas1.CurrentColor = color;
            SetActiveColorButton(tag);
            if (Canvas1.HasSelection) Canvas1.SetSelectedColor(color);
        }

        private void SetActiveColorButton(string tag)
        {
            foreach (var btn in new[] { ClrRed, ClrGold, ClrLime, ClrSky, ClrBlack, ClrWhite, ClrRedPanel, ClrGoldPanel, ClrLimePanel, ClrSkyPanel, ClrBlackPanel, ClrWhitePanel })
                btn.BorderThickness = (string)btn.Tag == tag ? new Thickness(3) : new Thickness(1);
        }

        private void Thickness_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            var tag = (string)btn.Tag;
            double thickness = double.Parse(tag);
            Canvas1.CurrentThickness = thickness;
            SetActiveThicknessButton(tag);
            if (Canvas1.HasSelection) Canvas1.SetSelectedThickness(thickness);
        }

        private void BtnFontSmaller_Click(object sender, RoutedEventArgs e)
        {
            Canvas1.CurrentFontSize = Math.Max(12, Canvas1.CurrentFontSize - 6);
            if (Canvas1.IsTextSelected) Canvas1.AdjustSelectedFontSize(-6);
        }

        private void BtnFontBigger_Click(object sender, RoutedEventArgs e)
        {
            Canvas1.CurrentFontSize = Math.Min(160, Canvas1.CurrentFontSize + 6);
            if (Canvas1.IsTextSelected) Canvas1.AdjustSelectedFontSize(6);
        }

        private void SetActiveThicknessButton(string tag)
        {
            foreach (var btn in new[] { ThinBtn, MidBtn, ThickBtn, ThinPanelBtn, MidPanelBtn, ThickPanelBtn })
                btn.Background = (string)btn.Tag == tag ? new SolidColorBrush(Color.FromRgb(0xE6, 0xF4, 0xEA)) : Brushes.Transparent;
        }

        private void FontSize_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            var tag = (string)btn.Tag;
            double fontSize = double.Parse(tag);
            Canvas1.CurrentFontSize = fontSize;
            SetActiveFontButton(tag);
            if (Canvas1.IsTextSelected) Canvas1.SetSelectedFontSize(fontSize);
        }

        private void SetActiveFontButton(string tag)
        {
            foreach (var btn in new[] { FontSmallBtn, FontMidBtn, FontLargeBtn, FontHugeBtn, FontSmallPanelBtn, FontMidPanelBtn, FontLargePanelBtn, FontHugePanelBtn })
                btn.Background = (string)btn.Tag == tag ? new SolidColorBrush(Color.FromRgb(0xE6, 0xF4, 0xEA)) : Brushes.Transparent;
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e) => Canvas1.Undo();
        private void BtnRedo_Click(object sender, RoutedEventArgs e) => Canvas1.Redo();
        private void BtnDelete_Click(object sender, RoutedEventArgs e) => Canvas1.DeleteSelected();
        private void BtnZoomIn_Click(object sender, RoutedEventArgs e) => Canvas1.Scale *= 1.25;
        private void BtnZoomOut_Click(object sender, RoutedEventArgs e) => Canvas1.Scale /= 1.25;
        private void BtnZoomReset_Click(object sender, RoutedEventArgs e) => Canvas1.Scale = 1.0;

        private void Canvas1_TextToolClicked(Point imagePoint)
        {
            var dlg = new TextInputDialog { Owner = this, InitialFontSize = Canvas1.CurrentFontSize };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResultText))
            {
                Canvas1.CurrentFontSize = dlg.ResultFontSize;
                Canvas1.AddTextAnnotation(imagePoint, dlg.ResultText);
                SetActiveFontButton(((int)dlg.ResultFontSize).ToString());
                SetActiveTool("Select");
                UpdateStatus("文字已添加。现在可以直接拖动，双击可继续编辑。");
            }
        }

        private void Canvas1_TextAnnotationDoubleClicked(TextAnnotation text)
        {
            var dlg = new TextInputDialog { Owner = this, InitialFontSize = text.FontSize, EditingExistingText = text.Text };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResultText))
            {
                Canvas1.CurrentFontSize = dlg.ResultFontSize;
                Canvas1.EditSelectedText(dlg.ResultText, dlg.ResultFontSize);
                SetActiveFontButton(((int)dlg.ResultFontSize).ToString());
            }
        }

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
            BtnSave.ContextMenu.PlacementTarget = BtnSave;
            BtnSave.ContextMenu.IsOpen = true;
        }

        private void MenuSaveAs_Click(object sender, RoutedEventArgs e) => SaveAnnotatedImage(false);

        private void MenuOverwrite_Click(object sender, RoutedEventArgs e) => BtnOverwrite_Click(sender, e);

        private void MenuCopy_Click(object sender, RoutedEventArgs e)
        {
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

            var result = MessageBox.Show(this,
                $"当前图片还有未保存的标注。要先保存再{actionName}吗？\n\n选择“是”会先保存；选择“否”会放弃这些标注；选择“取消”会留在当前图片。",
                "未保存的标注",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Cancel) return false;
            if (result == MessageBoxResult.No) return true;
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
