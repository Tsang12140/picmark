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
using System.Windows.Shell;
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
        private bool _fitToWindow = true;
        private bool _panMode;
        private bool _isPanningCanvas;
        private Point _panStartPoint;
        private double _panStartHorizontalOffset;
        private double _panStartVerticalOffset;
        private readonly DispatcherTimer _shapeHintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        private bool _initializingWatermarkUi = true;
        private WatermarkStyle _watermarkStyle = WatermarkStyle.DiamondGrid;
        private WatermarkLayout _watermarkLayout = WatermarkLayout.Tiled;
        private bool _watermarkBold;
        private Color _watermarkColor = Colors.Black;
        private string _watermarkFontFamily = "Microsoft YaHei UI";
        private bool _snappingWatermarkSlider;
        private bool _syncingWatermarkFromCanvas;
        private bool _hasSelectedWatermarkTemplate;

        public MainWindow()
        {
            InitializeComponent();

            // Win7 兼容：DWM 关闭时回退到标准窗口，移除 WindowChrome
            if (!Win7Helper.CanUseWindowChrome)
            {
                WindowChrome.SetWindowChrome(this, null);
                TitleBar.Visibility = Visibility.Collapsed;
            }

            _settings = AppSettings.Load();
            ApplyWindowSettings();
            Canvas1.AnnotationsChanged += (s, e) => MarkDirty();
            Canvas1.SelectionChanged += Canvas1_SelectionChanged;
            Canvas1.TextEditFinished += (s, e) => SetActiveTool("Select");
            Canvas1.ScaleChanged += (s, e) => UpdateZoomControls();
            Canvas1.WatermarkChanged += Canvas1_WatermarkChanged;
            Canvas1.PreviewMouseLeftButtonDown += (s, e) =>
            {
                ArrowPopup.IsOpen = false;
                MosaicPopup.IsOpen = false;
                ShapeHintPopup.IsOpen = false;
            };
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            Closing += MainWindow_Closing;
            StateChanged += MainWindow_StateChanged;
            SizeChanged += MainWindow_SizeChanged;
            Scroller.PreviewMouseWheel += Scroller_PreviewMouseWheel;
            Scroller.PreviewMouseLeftButtonDown += Scroller_PreviewMouseLeftButtonDown;
            Scroller.PreviewMouseMove += Scroller_PreviewMouseMove;
            Scroller.PreviewMouseLeftButtonUp += Scroller_PreviewMouseLeftButtonUp;
            _shapeHintTimer.Tick += ShapeHintTimer_Tick;
            ApplyOptionSettings();
            WatermarkTextBox.Text = BuildCertificateWatermarkText();
            _initializingWatermarkUi = false;
            UpdateWatermarkControls();
            HistoryCacheText.Text = _settings.HistoryCacheMb.ToString();
            UpdateHistoryCacheInfo();
            SetMosaicMode(MosaicMode.Pixelate);
            SetMosaicStrength(18);
            UpdateZoomControls();
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                BtnMaximize.ToolTip = "还原";
                MaximizeIcon.Visibility = Visibility.Collapsed;
                RestoreIcon.Visibility = Visibility.Visible;
            }
            else
            {
                BtnMaximize.ToolTip = "最大化";
                MaximizeIcon.Visibility = Visibility.Visible;
                RestoreIcon.Visibility = Visibility.Collapsed;
            }
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_fitToWindow && Canvas1.Image != null)
                FitImageAfterLayout();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Double-click toggles maximize/restore
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else if (e.ClickCount == 1)
            {
                try { DragMove(); }
                catch (InvalidOperationException) { }
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
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
            else if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add)) { _fitToWindow = false; Canvas1.Scale *= 1.25; e.Handled = true; }
            else if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract)) { _fitToWindow = false; Canvas1.Scale /= 1.25; e.Handled = true; }
            else if (ctrl && e.Key == Key.D0) { AutoFitZoom(); e.Handled = true; }
            else if (ctrl && e.Key == Key.D1) { _fitToWindow = false; Canvas1.Scale = 1.0; e.Handled = true; }
            else if ((e.Key == Key.Delete || e.Key == Key.Back) && Canvas1.HasSelection) { Canvas1.DeleteSelected(); e.Handled = true; }
        }

        private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Canvas1.Image == null) return;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                _fitToWindow = false;
                Canvas1.Scale *= e.Delta > 0 ? 1.15 : 1 / 1.15;
                e.Handled = true;
                return;
            }

            if (Canvas1.CurrentTool == AnnotationTool.Freehand ||
                Canvas1.CurrentTool == AnnotationTool.Rectangle ||
                Canvas1.CurrentTool == AnnotationTool.Ellipse ||
                Canvas1.CurrentTool == AnnotationTool.Arrow)
            {
                AdjustThickness(e.Delta > 0 ? 1 : -1);
                e.Handled = true;
            }
            else if (Canvas1.CurrentTool == AnnotationTool.Mosaic)
            {
                AdjustMosaicStrength(e.Delta > 0 ? 1 : -1);
                e.Handled = true;
            }
        }

        private void Scroller_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_panMode || Canvas1.Image == null) return;
            _isPanningCanvas = true;
            _panStartPoint = e.GetPosition(Scroller);
            _panStartHorizontalOffset = Scroller.HorizontalOffset;
            _panStartVerticalOffset = Scroller.VerticalOffset;
            Scroller.CaptureMouse();
            Scroller.Cursor = Cursors.Hand;
            e.Handled = true;
        }

        private void Scroller_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPanningCanvas) return;
            var point = e.GetPosition(Scroller);
            var delta = point - _panStartPoint;
            Scroller.ScrollToHorizontalOffset(_panStartHorizontalOffset - delta.X);
            Scroller.ScrollToVerticalOffset(_panStartVerticalOffset - delta.Y);
            e.Handled = true;
        }

        private void Scroller_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isPanningCanvas) return;
            _isPanningCanvas = false;
            Scroller.ReleaseMouseCapture();
            Scroller.Cursor = _panMode ? Cursors.Hand : null;
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
            string thickness = double.TryParse(_settings.Thickness, out _) ? _settings.Thickness : "9";
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

        private void EmptyState_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            BtnOpen_Click(sender, e);
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
                AppDialog.Show(this, "暂不支持这种图片格式，请使用 jpg、png、bmp 或 webp 格式。", "提示");
                return;
            }

            BitmapSource bmp;
            try
            {
                bmp = ext == ".webp" ? WebpDecoder.Load(path) : LoadStandardBitmap(path);
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, $"无法打开这张图片：{ex.Message}", "提示");
                return;
            }

            Canvas1.Image = bmp;
            Canvas1.ClearAll();
            SetImageWorkspaceVisible(true);
            _currentFilePath = path;
            _currentExtension = ext == ".webp" ? ".png" : ext;
            _hasUnsavedChanges = false;
            SetCurrentFileName(Path.GetFileName(path));
            FitImageAfterLayout();
            UpdateStatus($"已打开 {bmp.PixelWidth}×{bmp.PixelHeight}");
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
            SetImageWorkspaceVisible(true);
            _currentFilePath = null;
            _currentExtension = ".png";
            _hasUnsavedChanges = false;
            SetCurrentFileName("剪贴板图片.png");
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
            _fitToWindow = true;
            Thickness padding = CanvasHost?.Padding ?? new Thickness(0);
            double viewW = Math.Max(Scroller.ActualWidth - padding.Left - padding.Right, 200);
            double viewH = Math.Max(Scroller.ActualHeight - padding.Top - padding.Bottom, 200);
            double fit = Math.Min(viewW / Canvas1.Image.PixelWidth, viewH / Canvas1.Image.PixelHeight);
            Canvas1.Scale = fit;
            Dispatcher.BeginInvoke((Action)(() =>
            {
                Scroller.ScrollToHorizontalOffset(0);
                Scroller.ScrollToVerticalOffset(0);
            }), DispatcherPriority.Background);
        }

        private void SetImageWorkspaceVisible(bool hasImage)
        {
            CanvasFrame.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
            EmptyState.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
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
                _fitToWindow = false;
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

        private void BtnPanCanvas_Click(object sender, RoutedEventArgs e)
        {
            _panMode = !_panMode;
            if (_panMode)
            {
                Canvas1.CurrentTool = AnnotationTool.Select;
                ArrowPopup.IsOpen = false;
                Scroller.Cursor = Cursors.Hand;
                SetToolButtonState(new[] { BtnSelect, BtnRect, BtnEllipse, BtnArrow, BtnFreehand, BtnMosaic, BtnText }, "Pan", Brushes.Transparent);
                SetToolButtonState(new[] { PanelBtnRect, PanelBtnEllipse, PanelBtnArrow, PanelBtnFreehand, PanelBtnMosaic, PanelBtnText }, "Pan", new SolidColorBrush(Color.FromRgb(0x42, 0x42, 0x42)));
                BottomPanBtn.Background = (Brush)FindResource("ActiveBrush");
                BottomPanBtn.Foreground = Brushes.White;
                BottomPanBtn.BorderBrush = (Brush)FindResource("ActiveBorderBrush");
                BottomPanBtn.BorderThickness = new Thickness(2);
                UpdateStatus("抓手模式：按住图片区域拖动查看。");
            }
            else
            {
                Scroller.Cursor = null;
                SetActiveTool("Select");
                UpdateStatus("已切回选择/移动。");
            }
        }

        private void ToolButton_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var tag = (string)button.Tag;
            SetActiveTool(tag);
            if (tag == "Select")
                UpdateStatus("选择 / 移动 — 单击标注可选中，拖拽可移动。");
            if (tag == "Ellipse")
            {
                ShowShapeHint(button, "按住 Shift 画正圆");
                UpdateStatus("圈选：按住 Shift 可画正圆。");
            }
            if (tag == "Arrow")
            {
                ArrowPopup.PlacementTarget = button == PanelBtnArrow ? PanelBtnArrow : BtnArrow;
                ArrowPopup.IsOpen = true;
                UpdateStatus("箭头：不选样式也可直接画，默认是尖尾箭头。");
            }
            if (tag == "Mosaic")
            {
                MosaicPopup.PlacementTarget = button == PanelBtnMosaic ? PanelBtnMosaic : BtnMosaic;
                MosaicPopup.IsOpen = true;
                UpdateStatus("马赛克：可切换马赛克/模糊，并调整程度。");
            }
            if (tag == "Text")
                UpdateStatus("点击图片上的位置，然后直接输入文字；按 Enter 完成。");
        }

        private void BtnWatermark_Click(object sender, RoutedEventArgs e)
        {
            if (Canvas1.Image == null)
            {
                AppDialog.Show(this, "请先打开一张图片，再添加满屏水印。", "提示");
                return;
            }

            WatermarkPanel.Visibility = Visibility.Visible;
            SetActiveTool("Select");
            UpdateStatus("请选择一种水印样式；选中后会展开调节参数。");
        }

        private void BtnCloseWatermarkPanel_Click(object sender, RoutedEventArgs e) =>
            WatermarkPanel.Visibility = Visibility.Collapsed;

        private static string BuildCertificateWatermarkText() =>
            $"本证件/文件仅用于XX\n挪作他用无效 {DateTime.Now:yyyy年M月d日}";

        private void WatermarkTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button)) return;
            string template = button.Tag as string;
            _hasSelectedWatermarkTemplate = true;
            WatermarkParametersPanel.Visibility = Visibility.Visible;
            _initializingWatermarkUi = true;
            switch (template)
            {
                case "TiledText":
                    _watermarkStyle = WatermarkStyle.TextOnly;
                    _watermarkLayout = WatermarkLayout.Tiled;
                    WatermarkAngleSlider.Value = -25;
                    break;
                case "SingleText":
                    _watermarkStyle = WatermarkStyle.TextOnly;
                    _watermarkLayout = WatermarkLayout.Single;
                    WatermarkAngleSlider.Value = 0;
                    WatermarkOffsetSlider.Value = 0;
                    break;
                default:
                    _watermarkStyle = WatermarkStyle.DiamondGrid;
                    _watermarkLayout = WatermarkLayout.Tiled;
                    WatermarkTextBox.Text = BuildCertificateWatermarkText();
                    WatermarkAngleSlider.Value = 0;
                    break;
            }
            _initializingWatermarkUi = false;
            UpdateWatermarkControls();
            ApplyWatermarkFromControls(true);
        }

        private void WatermarkFont_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is string font)) return;
            _watermarkFontFamily = font;
            UpdateWatermarkControls();
            ApplyWatermarkFromControls();
        }

        private void WatermarkBold_Click(object sender, RoutedEventArgs e)
        {
            _watermarkBold = !_watermarkBold;
            UpdateWatermarkControls();
            ApplyWatermarkFromControls();
        }

        private void WatermarkColor_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button)) return;
            switch (button.Tag as string)
            {
                case "White": _watermarkColor = Colors.White; break;
                case "Red": _watermarkColor = Color.FromRgb(0xEA, 0x43, 0x35); break;
                case "Teal": _watermarkColor = Color.FromRgb(0x5C, 0x8D, 0x89); break;
                default: _watermarkColor = Colors.Black; break;
            }
            ApplyWatermarkFromControls();
        }

        private void WatermarkOption_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializingWatermarkUi) return;
            if (!_snappingWatermarkSlider && sender is Slider slider)
            {
                double snapRange = slider == WatermarkAngleSlider ? 3 : slider == WatermarkOffsetSlider ? 8 : 0;
                if (snapRange > 0 && Math.Abs(slider.Value) <= snapRange && Math.Abs(slider.Value) > 0.001)
                {
                    _snappingWatermarkSlider = true;
                    slider.Value = 0;
                    _snappingWatermarkSlider = false;
                }
            }
            UpdateWatermarkControls();
            ApplyWatermarkFromControls();
        }

        private void ApplyWatermarkFromControls(bool resetSinglePosition = false)
        {
            if (_initializingWatermarkUi || !_hasSelectedWatermarkTemplate || Canvas1 == null || Canvas1.Image == null) return;
            var current = Canvas1.GetWatermark();
            Canvas1.SetWatermark(new WatermarkSettings
            {
                Enabled = true,
                Text = WatermarkTextBox.Text,
                Style = _watermarkLayout == WatermarkLayout.Single ? WatermarkStyle.TextOnly : _watermarkStyle,
                Layout = _watermarkLayout,
                Opacity = WatermarkOpacitySlider.Value / 100.0,
                FontSize = WatermarkFontSizeSlider.Value,
                FontFamilyName = _watermarkFontFamily,
                Bold = _watermarkBold,
                Angle = WatermarkAngleSlider.Value,
                Spacing = WatermarkSpacingSlider.Value,
                HorizontalOffset = WatermarkOffsetSlider.Value,
                VerticalOffset = resetSinglePosition ? 0 : current?.VerticalOffset ?? 0,
                Color = _watermarkColor
            });
        }

        private void Canvas1_WatermarkChanged(object sender, EventArgs e)
        {
            if (_syncingWatermarkFromCanvas) return;
            var watermark = Canvas1.GetWatermark();
            if (watermark == null) return;
            _syncingWatermarkFromCanvas = true;
            _initializingWatermarkUi = true;
            WatermarkOffsetSlider.Value = Math.Max(WatermarkOffsetSlider.Minimum,
                Math.Min(WatermarkOffsetSlider.Maximum, watermark.HorizontalOffset));
            _initializingWatermarkUi = false;
            _syncingWatermarkFromCanvas = false;
            UpdateWatermarkControls();
        }

        private void UpdateWatermarkControls()
        {
            if (WatermarkOpacityValue == null) return;
            WatermarkOpacityValue.Text = $"{WatermarkOpacitySlider.Value:0}%";
            WatermarkFontSizeValue.Text = $"{WatermarkFontSizeSlider.Value:0}px";
            WatermarkAngleValue.Text = $"{WatermarkAngleSlider.Value:+0;-0;0}°";
            WatermarkSpacingValue.Text = $"{WatermarkSpacingSlider.Value:0}px";
            WatermarkOffsetValue.Text = $"{WatermarkOffsetSlider.Value:+0;-0;0}px";
            SetChoiceButtonState(WatermarkGridCard, _hasSelectedWatermarkTemplate && _watermarkStyle == WatermarkStyle.DiamondGrid);
            SetChoiceButtonState(WatermarkTiledCard, _hasSelectedWatermarkTemplate && _watermarkStyle == WatermarkStyle.TextOnly && _watermarkLayout == WatermarkLayout.Tiled);
            SetChoiceButtonState(WatermarkSingleCard, _hasSelectedWatermarkTemplate && _watermarkLayout == WatermarkLayout.Single);
            SetChoiceButtonState(WatermarkBoldBtn, _watermarkBold);
            SetChoiceButtonState(WatermarkFontYaHeiBtn, _watermarkFontFamily == "Microsoft YaHei UI");
            SetChoiceButtonState(WatermarkFontSimSunBtn, _watermarkFontFamily == "SimSun");
            SetChoiceButtonState(WatermarkFontSimHeiBtn, _watermarkFontFamily == "SimHei");
            SetChoiceButtonState(WatermarkFontKaiTiBtn, _watermarkFontFamily == "KaiTi");
            bool single = _watermarkLayout == WatermarkLayout.Single;
            WatermarkSpacingHeader.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
            WatermarkSpacingSlider.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
            SingleWatermarkHint.Visibility = single ? Visibility.Visible : Visibility.Collapsed;
            ResetSinglePositionBtn.Visibility = single ? Visibility.Visible : Visibility.Collapsed;
        }

        private void WatermarkReset_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button)) return;
            switch (button.Tag as string)
            {
                case "FontSize": WatermarkFontSizeSlider.Value = 28; break;
                case "Opacity": WatermarkOpacitySlider.Value = 20; break;
                case "Angle": WatermarkAngleSlider.Value = 0; break;
                case "Spacing": WatermarkSpacingSlider.Value = 204; break;
                case "Offset": WatermarkOffsetSlider.Value = 0; break;
            }
        }

        private void ResetSinglePosition_Click(object sender, RoutedEventArgs e)
        {
            WatermarkOffsetSlider.Value = 0;
            var current = Canvas1.GetWatermark();
            if (current == null) return;
            current.HorizontalOffset = 0;
            current.VerticalOffset = 0;
            Canvas1.SetWatermark(current);
        }

        private void BtnRemoveWatermark_Click(object sender, RoutedEventArgs e)
        {
            if (Canvas1.Image == null) return;
            _initializingWatermarkUi = true;
            _hasSelectedWatermarkTemplate = false;
            _initializingWatermarkUi = false;
            Canvas1.SetWatermark(null);
            WatermarkParametersPanel.Visibility = Visibility.Collapsed;
            UpdateWatermarkControls();
            UpdateStatus("已移除满屏水印。");
        }

        private void ArrowStyle_Click(object sender, RoutedEventArgs e)
        {
            var tag = (string)((Button)sender).Tag;
            if (Enum.TryParse(tag, out ArrowStyle style))
                Canvas1.CurrentArrowStyle = style;
            SetActiveTool("Arrow");
            ArrowPopup.IsOpen = false;
            UpdateStatus("已选择箭头样式，直接在图片上拖动绘制。");
        }

        private void ShowShapeHint(Button placementTarget, string text)
        {
            ShapeHintText.Text = text;
            ShapeHintPopup.PlacementTarget = placementTarget;
            ShapeHintPopup.IsOpen = true;
            _shapeHintTimer.Stop();
            _shapeHintTimer.Start();
        }

        private void ShapeHintTimer_Tick(object sender, EventArgs e)
        {
            _shapeHintTimer.Stop();
            ShapeHintPopup.IsOpen = false;
        }

        private void SetActiveTool(string tag)
        {
            _panMode = false;
            _isPanningCanvas = false;
            Scroller.Cursor = null;
            if (tag != "Arrow") ArrowPopup.IsOpen = false;
            if (tag != "Mosaic") MosaicPopup.IsOpen = false;
            Canvas1.CurrentTool = (AnnotationTool)Enum.Parse(typeof(AnnotationTool), tag);
            SetToolButtonState(new[] { BtnSelect, BtnRect, BtnEllipse, BtnArrow, BtnFreehand, BtnMosaic, BtnText }, tag, Brushes.Transparent);
            SetToolButtonState(new[] { PanelBtnRect, PanelBtnEllipse, PanelBtnArrow, PanelBtnFreehand, PanelBtnMosaic, PanelBtnText }, tag, new SolidColorBrush(Color.FromRgb(0x42, 0x42, 0x42)));
            BottomPanBtn.Background = Brushes.Transparent;
            BottomPanBtn.Foreground = (Brush)FindResource("TextPrimaryBrush");
            BottomPanBtn.BorderBrush = (Brush)FindResource("BorderBrush1");
            BottomPanBtn.BorderThickness = new Thickness(1);
            _settings.Tool = tag;
        }

        private void Color_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            Color selectedColor = SetColorOption((string)btn.Tag);
            if (Canvas1.HasSelection) Canvas1.SetSelectedColor(selectedColor);
        }

        private void ColorPicker_Click(object sender, RoutedEventArgs e)
        {
            var current = Canvas1.CurrentColor;
            using (var dialog = new System.Windows.Forms.ColorDialog())
            {
                dialog.FullOpen = true;
                dialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

                var color = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
                string tag = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                SetColorOption(tag);
                if (Canvas1.HasSelection) Canvas1.SetSelectedColor(color);
            }
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
            if (ClrCustomPanel != null && tag.StartsWith("#"))
            {
                ClrCustomPanel.Background = new SolidColorBrush(selectedColor);
                ClrCustomPanel.Foreground = IsLightColor(selectedColor) ? Brushes.Black : Brushes.White;
                ClrCustomPanel.BorderThickness = new Thickness(2);
            }
            _settings.Color = tag;
            return selectedColor;
        }

        private void SetActiveColorButton(string tag)
        {
            foreach (var btn in new[] { ClrRed, ClrGold, ClrLime, ClrSky, ClrBlack, ClrWhite, ClrRedPanel, ClrGoldPanel, ClrLimePanel, ClrSkyPanel, ClrBlackPanel, ClrWhitePanel })
                btn.BorderThickness = (string)btn.Tag == tag ? new Thickness(3) : new Thickness(1);
            if (ClrCustomPanel != null && !tag.StartsWith("#"))
            {
                ClrCustomPanel.Background = Brushes.Transparent;
                ClrCustomPanel.Foreground = (Brush)FindResource("TextPrimaryBrush");
                ClrCustomPanel.BorderThickness = new Thickness(0);
            }
        }

        private static bool IsLightColor(Color color) =>
            color.R * 0.299 + color.G * 0.587 + color.B * 0.114 > 170;

        private void Thickness_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            double thickness = SetThicknessOption((string)btn.Tag);
            if (Canvas1.HasSelection) Canvas1.SetSelectedThickness(thickness);
        }

        private double SetThicknessOption(string tag)
        {
            if (!double.TryParse(tag, out double thickness)) thickness = 6;
            thickness = Math.Max(1, Math.Min(20, Math.Round(thickness)));
            if (Canvas1 != null) Canvas1.CurrentThickness = thickness;
            if (ThicknessSlider != null && Math.Abs(ThicknessSlider.Value - thickness) > 0.001)
                ThicknessSlider.Value = thickness;
            if (ThicknessValueText != null)
                ThicknessValueText.Text = ((int)thickness).ToString();
            if (_settings != null) _settings.Thickness = ((int)thickness).ToString();
            return thickness;
        }

        private void ThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            double thickness = SetThicknessOption(Math.Round(e.NewValue).ToString());
            if (Canvas1 != null && Canvas1.HasSelection) Canvas1.SetSelectedThickness(thickness);
        }

        private void ThicknessSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            AdjustThickness(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }

        private void AdjustThickness(int delta)
        {
            double next = Math.Max(1, Math.Min(20, Canvas1.CurrentThickness + delta));
            double thickness = SetThicknessOption(next.ToString());
            if (Canvas1.HasSelection) Canvas1.SetSelectedThickness(thickness);
            UpdateStatus($"画笔大小：{(int)thickness}");
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

        private void MosaicMode_Click(object sender, RoutedEventArgs e)
        {
            string tag = (string)((Button)sender).Tag;
            SetMosaicMode(tag == "Blur" ? MosaicMode.Blur : MosaicMode.Pixelate);
        }

        private void SetMosaicMode(MosaicMode mode)
        {
            Canvas1.CurrentMosaicMode = mode;
            if (MosaicModePixelBtn != null)
                SetChoiceButtonState(MosaicModePixelBtn, mode == MosaicMode.Pixelate);
            if (MosaicModeBlurBtn != null)
                SetChoiceButtonState(MosaicModeBlurBtn, mode == MosaicMode.Blur);
            UpdateStatus(mode == MosaicMode.Blur ? "马赛克工具：模糊模式。" : "马赛克工具：像素马赛克模式。");
        }

        private void MosaicStrengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            SetMosaicStrength((int)Math.Round(e.NewValue));
        }

        private void MosaicStrengthSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            AdjustMosaicStrength(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }

        private void AdjustMosaicStrength(int delta)
        {
            SetMosaicStrength(Canvas1.CurrentMosaicStrength + delta);
            UpdateStatus($"马赛克/模糊程度：{Canvas1.CurrentMosaicStrength}");
        }

        private void SetMosaicStrength(int strength)
        {
            strength = Math.Max(2, Math.Min(30, strength));
            if (Canvas1 != null) Canvas1.CurrentMosaicStrength = strength;
            if (MosaicStrengthSlider != null && Math.Abs(MosaicStrengthSlider.Value - strength) > 0.001)
                MosaicStrengthSlider.Value = strength;
            if (MosaicStrengthText != null)
                MosaicStrengthText.Text = strength.ToString();
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e) => Canvas1.Undo();
        private void BtnRedo_Click(object sender, RoutedEventArgs e) => Canvas1.Redo();
        private void BtnDelete_Click(object sender, RoutedEventArgs e) => Canvas1.DeleteSelected();
        private void BtnZoomIn_Click(object sender, RoutedEventArgs e) { _fitToWindow = false; Canvas1.Scale *= 1.25; }
        private void BtnZoomOut_Click(object sender, RoutedEventArgs e) { _fitToWindow = false; Canvas1.Scale /= 1.25; }
        private void BtnZoomReset_Click(object sender, RoutedEventArgs e) { _fitToWindow = false; Canvas1.Scale = 1.0; }

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
                AppDialog.Show(this, "请先打开一张图片。", "提示");
                return;
            }

            Clipboard.SetImage(Canvas1.RenderFullResolution());
            UpdateStatus("已复制标注后的图片到剪贴板");
        }

        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            if (Canvas1.Image == null)
            {
                AppDialog.Show(this, "请先打开一张图片。", "提示");
                return;
            }

            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true) return;

            var bitmap = Canvas1.RenderFullResolution();
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                double scale = Math.Min(printDialog.PrintableAreaWidth / bitmap.PixelWidth, printDialog.PrintableAreaHeight / bitmap.PixelHeight);
                double width = bitmap.PixelWidth * scale;
                double height = bitmap.PixelHeight * scale;
                double x = (printDialog.PrintableAreaWidth - width) / 2;
                double y = (printDialog.PrintableAreaHeight - height) / 2;
                dc.DrawImage(bitmap, new Rect(x, y, width, height));
            }

            printDialog.PrintVisual(visual, "PicMark 图片");
            UpdateStatus("已发送到打印机。");
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnOverwrite_Click(object sender, RoutedEventArgs e)
        {
            if (_currentFilePath == null)
            {
                AppDialog.Show(this, "这张图片还没有对应的原文件路径（比如是粘贴来的截图），无法覆盖，请用“保存”另存为新文件。", "提示");
                return;
            }
            if (Canvas1.HasWatermark)
            {
                var choice = WatermarkOverwriteDialog.Show(this);
                if (choice == WatermarkOverwriteChoice.SaveAs)
                    SaveAnnotatedImage(false);
                else if (choice == WatermarkOverwriteChoice.Overwrite)
                    SaveAnnotatedImage(true);
                return;
            }
            var result = AppDialog.Show(this, "确定要覆盖原图吗？原图将被替换，无法恢复。",
                "确认覆盖原图", MessageBoxButton.YesNo);
            if (result == MessageBoxResult.Yes) SaveAnnotatedImage(true);
        }

        private bool SaveAnnotatedImage(bool overwrite)
        {
            if (Canvas1.Image == null)
            {
                AppDialog.Show(this, "请先打开一张图片。", "提示");
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
                    : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string baseName = _currentFilePath != null
                    ? Path.GetFileNameWithoutExtension(_currentFilePath) + "_标注"
                    : "粘贴图片_标注";
                var options = new SaveOptionsDialog(Canvas1.RenderFullResolution(), _currentFilePath, ext)
                {
                    Owner = this
                };
                if (options.ShowDialog() != true) return false;
                targetPath = options.TargetPath;
                ext = options.TargetExtension;
                var rendered = Canvas1.RenderFullResolution();
                SaveBitmapWithOptions(rendered, targetPath, ext, options.OutputWidth, options.OutputHeight, options.Quality, options.TargetBytes);
                _hasUnsavedChanges = false;
                UpdateStatus($"已保存：{targetPath}（原图未改动）");
                return true;
            }

            var rtb = Canvas1.RenderFullResolution();
            SaveBitmapWithOptions(rtb, targetPath, ext, rtb.PixelWidth, rtb.PixelHeight, 95, null);

            _hasUnsavedChanges = false;
            UpdateStatus(overwrite ? $"已保存（已覆盖原图）：{targetPath}" : $"已保存：{targetPath}（原图未改动）");
            return true;
        }

        private static void SaveBitmapWithOptions(BitmapSource source, string targetPath, string ext, int width, int height, int quality, long? targetBytes)
        {
            BitmapSource resized = ResizeBitmap(source, width, height);
            byte[] bytes = EncodeBitmap(resized, ext, quality);

            if (targetBytes.HasValue)
            {
                if (ext == ".jpg" || ext == ".jpeg")
                {
                    int low = 35;
                    int high = Math.Max(35, Math.Min(quality, 98));
                    byte[] best = bytes;
                    for (int i = 0; i < 7; i++)
                    {
                        int q = (low + high) / 2;
                        byte[] candidate = EncodeBitmap(resized, ext, q);
                        if (candidate.Length > targetBytes.Value)
                            high = q - 1;
                        else
                        {
                            best = candidate;
                            low = q + 1;
                        }
                    }
                    bytes = best;
                }
                else
                {
                    double scale = 0.92;
                    while (bytes.Length > targetBytes.Value && width > 120 && height > 120)
                    {
                        width = Math.Max(1, (int)(width * scale));
                        height = Math.Max(1, (int)(height * scale));
                        resized = ResizeBitmap(source, width, height);
                        bytes = EncodeBitmap(resized, ext, quality);
                    }
                }
            }

            File.WriteAllBytes(targetPath, bytes);
        }

        private static BitmapSource ResizeBitmap(BitmapSource source, int width, int height)
        {
            if (source.PixelWidth == width && source.PixelHeight == height) return source;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                dc.DrawImage(source, new Rect(0, 0, width, height));
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        private static byte[] EncodeBitmap(BitmapSource source, string ext, int quality)
        {
            BitmapEncoder encoder;
            if (ext == ".jpg" || ext == ".jpeg")
                encoder = new JpegBitmapEncoder { QualityLevel = Math.Max(35, Math.Min(98, quality)) };
            else if (ext == ".bmp")
                encoder = new BmpBitmapEncoder();
            else
                encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var ms = new MemoryStream())
            {
                encoder.Save(ms);
                return ms.ToArray();
            }
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

        private void BtnOptions_Click(object sender, RoutedEventArgs e)
        {
            OptionsPopup.IsOpen = true;
        }

        private void MenuHistoryCache_Click(object sender, RoutedEventArgs e)
        {
            OptionsPopup.IsOpen = false;
            ShowHistoryCacheDialog();
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

        private void ApplyHistoryCacheSetting(bool trimNow, string cacheText)
        {
            HistoryCacheText.Text = cacheText;
            ApplyHistoryCacheSetting(trimNow);
        }

        private void UpdateHistoryCacheInfo()
        {
            if (HistoryCacheInfoText == null) return;
            double usedMb = HistoryManager.GetCacheBytes() / 1024.0 / 1024.0;
            HistoryCacheInfoText.Text = $"当前约 {usedMb:0.#} MB / 上限 {_settings.HistoryCacheMb} MB";
        }

        private void ShowHistoryCacheDialog()
        {
            double usedMb = HistoryManager.GetCacheBytes() / 1024.0 / 1024.0;
            var dialog = new Window
            {
                Owner = this,
                Title = "历史缓存",
                Width = 360,
                Height = 220,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = (Brush)FindResource("PanelBrush")
            };

            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(new TextBlock
            {
                Text = "历史缓存",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                Margin = new Thickness(0, 0, 0, 14)
            });
            root.Children.Add(new TextBlock
            {
                Text = $"当前约 {usedMb:0.#} MB / 上限 {_settings.HistoryCacheMb} MB",
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 0, 0, 8)
            });

            var box = new TextBox
            {
                Text = _settings.HistoryCacheMb.ToString(),
                Style = (Style)FindResource("PanelTextBox"),
                Margin = new Thickness(0, 0, 0, 14)
            };
            root.Children.Add(box);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var openButton = new Button { Content = "打开目录", Style = (Style)FindResource("ToolButton"), MinWidth = 78 };
            var applyButton = new Button { Content = "应用", Style = (Style)FindResource("PrimaryButton"), MinWidth = 72 };
            buttons.Children.Add(openButton);
            buttons.Children.Add(applyButton);
            root.Children.Add(buttons);

            openButton.Click += (s, e) =>
            {
                Directory.CreateDirectory(HistoryManager.HistoryDirectory);
                Process.Start(HistoryManager.HistoryDirectory);
            };
            applyButton.Click += (s, e) =>
            {
                ApplyHistoryCacheSetting(true, box.Text);
                _settings.Save();
                UpdateHistoryCacheInfo();
                dialog.DialogResult = true;
            };

            dialog.Content = root;
            dialog.ShowDialog();
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

            var result = AppDialog.Show(this,
                $"当前图片还有未保存的标注。\n{historyLine}\n\n要另存为新图片再{actionName}吗？\n\n是：先另存为\n否：不另存，继续{actionName}\n取消：留在当前图片",
                "未保存的标注",
                MessageBoxButton.YesNoCancel);

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

        private void SetCurrentFileName(string name)
        {
            string displayName = ShortenFileName(name);
            TopFileNameText.Text = displayName;
            TopFileNameText.ToolTip = name;
            CurrentFileText.Text = displayName;
            CurrentFileText.ToolTip = name;
        }

        private void UpdateStatus(string text) => StatusText.Text = text;

        private static string ShortenFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length <= 28) return name;
            string extension = Path.GetExtension(name);
            string stem = Path.GetFileNameWithoutExtension(name);
            if (stem.Length <= 18) return name;
            return stem.Substring(0, 18) + "..." + extension;
        }
    }
}
