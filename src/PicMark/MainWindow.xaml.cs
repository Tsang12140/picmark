using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using System.Windows.Shell;
using Microsoft.Win32;

namespace PicMark
{
    public partial class MainWindow : Window
    {
        private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };
        private static readonly string[] SaveableExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };
        private const string ProjectExtension = ".picmark";
        private const double RightEditPanelWidth = 342;
        // 最小窗口尺寸按 WorkArea 自适应：大屏保持 980×640，小屏（高 DPI 缩放）缩到 96% 工作区。
        private static double MinimumWindowWidth => Math.Max(760, Math.Min(980, SystemParameters.WorkArea.Width * 0.96));
        private static double MinimumWindowHeight => Math.Max(520, Math.Min(640, SystemParameters.WorkArea.Height * 0.96));

        private string _currentFilePath;
        private string _currentProjectPath;
        private string _currentExtension = ".png";

        // The recent-files start page owns at most eight small previews.
        // Keeping the cache session-local avoids re-decoding large local files during resize.
        private readonly Dictionary<string, BitmapSource> _recentFileThumbnailCache = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
        private int _recentFilesVisibleLimit = -1;
        private double _recentFilesListWidth = -1;

        private readonly List<string> _batchFiles = new List<string>();
        private int _batchIndex = -1;
        private const string BatchWatermarkOutputFolderName = "PicMark_水印输出";
        private readonly List<string> _batchWatermarkFiles = new List<string>();
        private int _batchWatermarkIndex = -1;
        private bool _batchWatermarkMode;
        private bool _batchWatermarkProcessing;
        private bool _preserveEditorStateBatch;
        private EditorSessionState _editorSessionState;
        private readonly List<string> _viewerFiles = new List<string>();
        private int _viewerIndex = -1;
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
        private readonly DispatcherTimer _watermarkDefaultsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
        private bool _initializingWatermarkUi = true;
        private WatermarkStyle _watermarkStyle = WatermarkStyle.DiamondGrid;
        private WatermarkLayout _watermarkLayout = WatermarkLayout.Tiled;
        private bool _watermarkBold;
        private Color _watermarkColor = Colors.Black;
        private bool _watermarkColorUserSelected;
        private string _watermarkFontFamily = "Microsoft YaHei UI";
        private string _watermarkLogoPath = string.Empty;
        private bool _watermarkLogoFlipHorizontal;
        private bool _watermarkLogoFlipVertical;
        private bool _snappingWatermarkSlider;
        private bool _syncingWatermarkFromCanvas;
        private bool _hasSelectedWatermarkTemplate;
        private bool _loadingDocument;
        private bool _editMode;
        private bool _viewerBottomHot;
        private bool _viewerLeftHot;
        private bool _viewerRightHot;
        private UpdateCheckResult _lastUpdateCheck;
        private string _telemetryUrl;
        private bool _syncingAnnotationProperties;

        private sealed class EditorSessionState
        {
            public BatchWatermarkProfile WatermarkProfile { get; set; }
            public AnnotationCanvas.CropSessionState CropSession { get; set; }
            public bool WatermarkActive { get; set; }
            public bool CropActive { get; set; }
        }

        private sealed class OpenWithApplication
        {
            public string DisplayName { get; set; }
            public string ExecutablePath { get; set; }
            public string Arguments { get; set; }
        }

        public MainWindow()
        {
            InitializeComponent();
            ApplyChromeTheme(false);

            // Win7 兼容：DWM 关闭时回退到标准窗口，移除 WindowChrome
            if (!Win7Helper.CanUseWindowChrome)
            {
                WindowChrome.SetWindowChrome(this, null);
                TitleBar.Visibility = Visibility.Collapsed;
            }

            _settings = AppSettings.Load();
            UpdateTitleUpdateUi();
            EnsureInitialWindowLayout();
            ApplyWindowSettings();
            Canvas1.AnnotationsChanged += (s, e) => { if (!_loadingDocument) MarkDirty(); };
            Canvas1.SelectionChanged += Canvas1_SelectionChanged;
            Canvas1.TextEditFinished += (s, e) => SetActiveTool("Select");
            Canvas1.ScaleChanged += (s, e) => UpdateZoomControls();
            Canvas1.WatermarkChanged += Canvas1_WatermarkChanged;
            Canvas1.IsEditingEnabled = false;
            Canvas1.PreviewMouseLeftButtonDown += (s, e) =>
            {
                ArrowPopup.IsOpen = false;
                ShapeHintPopup.IsOpen = false;
            };
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            Closing += MainWindow_Closing;
            StateChanged += MainWindow_StateChanged;
            SizeChanged += MainWindow_SizeChanged;
            Loaded += (s, e) => UpdateRecentFilesUi(false);
            DpiChanged += MainWindow_DpiChanged;
            Scroller.PreviewMouseWheel += Scroller_PreviewMouseWheel;
            Scroller.PreviewMouseLeftButtonDown += Scroller_PreviewMouseLeftButtonDown;
            Scroller.PreviewMouseMove += Scroller_PreviewMouseMove;
            Scroller.PreviewMouseLeftButtonUp += Scroller_PreviewMouseLeftButtonUp;
            ImageStage.MouseMove += ImageStage_MouseMove;
            ImageStage.MouseLeave += ImageStage_MouseLeave;
            _shapeHintTimer.Tick += ShapeHintTimer_Tick;
            _watermarkDefaultsSaveTimer.Tick += WatermarkDefaultsSaveTimer_Tick;
            ApplyOptionSettings();
            ApplyWatermarkPresetToControls(_settings.LastWatermarkPreset);
            _initializingWatermarkUi = false;
            UpdateWatermarkControls();
            HistoryCacheText.Text = _settings.HistoryCacheMb.ToString();
            UpdateHistoryCacheInfo();
            UpdateRecentFilesUi();
            UpdateRecentLogoUi();
            UpdateWatermarkTextHistoryUi();
            SetMosaicMode(MosaicMode.Pixelate);
            SetMosaicStrength(18);
            SetImageWorkspaceVisible(false);
            UpdateRecentContextActionUi();
            UpdateZoomControls();
            StartOnlineServicesTimer();
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
            ApplyWindowFrame(_editMode);
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_fitToWindow && Canvas1.Image != null)
                FitImageAfterLayout();
            UpdateBottomOverlayConstraints();
            if (Canvas1.Image == null)
                UpdateRecentFilesUi(false);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateBottomOverlayConstraints();
                if (Canvas1.Image == null)
                    UpdateRecentFilesUi(false);
            }), DispatcherPriority.Loaded);
        }

        private void MainWindow_DpiChanged(object sender, DpiChangedEventArgs e)
        {
            // 跨显示器拖动导致 DPI 变化后，重新 clamp 窗口尺寸/位置到当前工作区
            var work = SystemParameters.WorkArea;
            double minW = MinimumWindowWidth;
            double minH = MinimumWindowHeight;

            MinWidth = minW;
            MinHeight = minH;

            if (WindowState == WindowState.Normal)
            {
                double w = Math.Min(Math.Max(minW, Width), work.Width);
                double h = Math.Min(Math.Max(minH, Height), work.Height);
                Width = w;
                Height = h;

                double left = Math.Max(work.Left, Math.Min(Left, work.Right - Math.Min(w, 200)));
                double top = Math.Max(work.Top, Math.Min(Top, work.Bottom - 40));
                Left = left;
                Top = top;
            }
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
            var existing = paths?
                .Where(File.Exists)
                .ToArray();

            if (existing == null || existing.Length == 0) return;

            var projectPath = existing.FirstOrDefault(IsProjectPath);
            if (projectPath != null)
            {
                ClearBatch();
                LoadProject(projectPath);
                return;
            }

            var files = existing
                .Where(IsSupportedImagePath)
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
            else if (ctrl && shift && e.Key == Key.S) { MenuSaveAs_Click(this, null); e.Handled = true; }
            else if (ctrl && e.Key == Key.S) { BtnSave_Click(this, null); e.Handled = true; }
            else if (ctrl && e.Key == Key.O) { BtnOpen_Click(this, null); e.Handled = true; }
            else if (ctrl && e.Key == Key.P) { BtnPrint_Click(this, null); e.Handled = true; }
            else if (ctrl && e.Key == Key.E && Canvas1.Image != null && !IsTextInputFocused()) { SetEditMode(!_editMode, true); e.Handled = true; }
            else if (ctrl && e.Key == Key.C && Canvas1.Image != null && !IsTextInputFocused()) { MenuCopy_Click(this, null); e.Handled = true; }
            else if (ctrl && e.Key == Key.V) { PasteFromClipboard(); e.Handled = true; }
            else if (ctrl && e.Key == Key.D) { Canvas1.Deselect(); e.Handled = true; }
            else if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add)) { _fitToWindow = false; Canvas1.Scale *= 1.25; e.Handled = true; }
            else if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract)) { _fitToWindow = false; Canvas1.Scale /= 1.25; e.Handled = true; }
            else if (ctrl && e.Key == Key.D0) { AutoFitZoom(); e.Handled = true; }
            else if (ctrl && e.Key == Key.D1) { _fitToWindow = false; Canvas1.Scale = 1.0; e.Handled = true; }
            else if (!ctrl && !shift && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && !_editMode && Canvas1.Image != null && !IsTextInputFocused() && e.Key == Key.Left)
            {
                GoToViewerIndex(_viewerIndex - 1);
                e.Handled = true;
            }
            else if (!ctrl && !shift && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && !_editMode && Canvas1.Image != null && !IsTextInputFocused() && e.Key == Key.Right)
            {
                GoToViewerIndex(_viewerIndex + 1);
                e.Handled = true;
            }
            else if ((e.Key == Key.Delete || e.Key == Key.Back) && Canvas1.HasSelection) { Canvas1.DeleteSelected(); e.Handled = true; }
            else if (Canvas1.CurrentTool == AnnotationTool.Crop && e.Key == Key.Enter) { ConfirmCropAction(); e.Handled = true; }
            else if (Canvas1.CurrentTool == AnnotationTool.Crop && e.Key == Key.Escape) { Canvas1.CancelCrop(); SetActiveTool("Select"); e.Handled = true; }
        }

        private static bool IsTextInputFocused()
        {
            DependencyObject focused = Keyboard.FocusedElement as DependencyObject;
            while (focused != null)
            {
                if (focused is TextBox || focused is PasswordBox || focused is ComboBox)
                    return true;
                focused = VisualTreeHelper.GetParent(focused);
            }
            return false;
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
            if (Canvas1.TryBeginWatermarkDrag(e.GetPosition(Canvas1)))
            {
                e.Handled = true;
                return;
            }
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

        private void EnsureInitialWindowLayout()
        {
            if (_settings.WindowLayoutInitialized) return;

            _settings.WindowLeft = double.NaN;
            _settings.WindowTop = double.NaN;
            _settings.WindowWidth = 1080;
            _settings.WindowHeight = 720;
            _settings.WindowState = WindowState.Normal;
            _settings.WindowLayoutInitialized = true;
            _settings.Save();
        }

        private void ApplyWindowSettings()
        {
            var work = SystemParameters.WorkArea;
            double minW = MinimumWindowWidth;
            double minH = MinimumWindowHeight;

            MinWidth = minW;
            MinHeight = minH;

            // clamp 保存的窗口尺寸到当前工作区，防止小屏上窗口超出
            double w = Math.Min(Math.Max(minW, _settings.WindowWidth), work.Width);
            double h = Math.Min(Math.Max(minH, _settings.WindowHeight), work.Height);
            Width = w;
            Height = h;

            if (!double.IsNaN(_settings.WindowLeft) && !double.IsNaN(_settings.WindowTop))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                // 确保标题栏可见：左上不超出工作区，右下留出至少 200px 可见区域
                double left = Math.Max(work.Left, Math.Min(_settings.WindowLeft, work.Right - Math.Min(w, 200)));
                double top = Math.Max(work.Top, Math.Min(_settings.WindowTop, work.Bottom - 40));
                Left = left;
                Top = top;
            }

            if (_settings.WindowState == WindowState.Maximized)
                WindowState = WindowState.Maximized;
        }

        private void ApplyOptionSettings()
        {
            string tool = Enum.TryParse(_settings.Tool, out AnnotationTool _) && _settings.Tool != "Crop" ? _settings.Tool : "Select";
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
            SaveWatermarkDefaultsNow();
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
            if (files == null || files.Length == 0) return;

            var imageFiles = files.Where(File.Exists).Where(IsSupportedImagePath).ToArray();
            if (HasReusableEditorSession() && imageFiles.Length == files.Length)
            {
                StartPreservedEditorSession(imageFiles);
                return;
            }

            if (!ConfirmDiscardUnsavedChanges("\u6253\u5f00\u65b0\u56fe\u7247")) return;
            if (files.Length == 1 && IsProjectPath(files[0]))
            {
                ClearBatch();
                LoadProject(files[0]);
                return;
            }

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
                Filter = "PicMark 项目或图片|*.picmark;*.jpg;*.jpeg;*.png;*.bmp;*.webp|PicMark 项目|*.picmark|图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.webp|所有文件|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                if (!ConfirmDiscardUnsavedChanges("打开新图片")) return;
                ClearBatch();
                if (IsProjectPath(dlg.FileName))
                    LoadProject(dlg.FileName);
                else
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
            _preserveEditorStateBatch = false;
            _editorSessionState = null;
            _batchFiles.Clear();
            _batchFiles.AddRange(files);
            _batchIndex = 0;
            SetViewerFiles(_batchFiles, _batchFiles[_batchIndex]);
            LoadImage(_batchFiles[_batchIndex], false);
            UpdateBatchBar();
        }

        private void ClearBatch()
        {
            _batchFiles.Clear();
            _batchIndex = -1;
            _preserveEditorStateBatch = false;
            _editorSessionState = null;
            UpdateBatchBar();
        }

        private bool HasReusableEditorSession()
        {
            if (!_editMode || _batchWatermarkMode || Canvas1.Image == null) return false;
            bool watermarkActive = WatermarkPanel.Visibility == Visibility.Visible && Canvas1.GetWatermark()?.Enabled == true;
            return watermarkActive || Canvas1.CurrentTool == AnnotationTool.Crop;
        }

        private EditorSessionState CaptureEditorSessionState()
        {
            var watermark = Canvas1.GetWatermark();
            return new EditorSessionState
            {
                WatermarkActive = WatermarkPanel.Visibility == Visibility.Visible && watermark?.Enabled == true,
                CropActive = Canvas1.CurrentTool == AnnotationTool.Crop,
                WatermarkProfile = BatchWatermarkProfile.Create(watermark, Canvas1.Image),
                CropSession = Canvas1.CaptureCropSession()
            };
        }

        private void StartPreservedEditorSession(IEnumerable<string> paths)
        {
            var files = (paths ?? Enumerable.Empty<string>())
                .Where(File.Exists)
                .Where(IsSupportedImagePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (files.Count == 0) return;

            _editorSessionState = CaptureEditorSessionState();
            _preserveEditorStateBatch = true;
            _batchFiles.Clear();
            _batchFiles.AddRange(files);
            _batchIndex = 0;
            SetViewerFiles(_batchFiles, _batchFiles[0]);
            LoadImageWithEditorSession(_batchFiles[0], _editorSessionState);
            UpdateBatchBar();
            UpdateStatus("\u5df2\u6362\u6210\u65b0\u56fe\uff0c\u6c34\u5370\u6216\u88c1\u5207\u8bbe\u7f6e\u5df2\u4fdd\u7559\u3002");
        }

        private void LoadImageWithEditorSession(string path, EditorSessionState state)
        {
            LoadImage(path, false);
            if (Canvas1.Image == null || state == null) return;

            SetEditMode(true, false);
            if (state.WatermarkProfile != null)
            {
                var watermark = state.WatermarkProfile.CreateFor(Canvas1.Image);
                Canvas1.SetWatermark(watermark, notifyChanged: false, notifyWatermarkChanged: true);
                _hasSelectedWatermarkTemplate = watermark != null;
            }

            WatermarkPanel.Visibility = state.WatermarkActive ? Visibility.Visible : Visibility.Collapsed;
            WatermarkParametersPanel.Visibility = state.WatermarkActive ? Visibility.Visible : Visibility.Collapsed;
            if (state.CropActive)
            {
                SetActiveTool("Crop");
                Canvas1.RestoreCropSession(state.CropSession);
            }
            else
            {
                SetActiveTool("Select");
            }

            _hasUnsavedChanges = false;
            UpdateWatermarkControls();
            UpdateViewerNavigation();
        }

        private void BtnPrevImage_Click(object sender, RoutedEventArgs e) => GoToBatchIndex(_batchIndex - 1);
        private void BtnNextImage_Click(object sender, RoutedEventArgs e) => GoToBatchIndex(_batchIndex + 1);

        private void GoToBatchIndex(int newIndex)
        {
            if (newIndex < 0 || newIndex >= _batchFiles.Count) return;
            if (newIndex == _batchIndex) return;

            if (_preserveEditorStateBatch)
            {
                _editorSessionState = CaptureEditorSessionState();
                _batchIndex = newIndex;
                SetViewerFiles(_batchFiles, _batchFiles[_batchIndex]);
                LoadImageWithEditorSession(_batchFiles[_batchIndex], _editorSessionState);
                UpdateBatchBar();
                return;
            }

            if (_hasUnsavedChanges)
            {
                if (!ConfirmDiscardUnsavedChanges("切换图片")) return;
            }

            _batchIndex = newIndex;
            SetViewerFiles(_batchFiles, _batchFiles[_batchIndex]);
            LoadImage(_batchFiles[_batchIndex], false);
            UpdateBatchBar();
        }

        private void UpdateBatchBar()
        {
            if (_batchWatermarkMode)
            {
                BatchBar.Visibility = Visibility.Collapsed;
                return;
            }

            if (_batchFiles.Count > 1)
            {
                BatchBar.Visibility = Visibility.Visible;
                BatchPositionText.Text = _preserveEditorStateBatch
                    ? $"\u7edf\u4e00\u8bbe\u7f6e\uff1a\u7b2c {_batchIndex + 1} \u5f20\uff0c\u5171 {_batchFiles.Count} \u5f20\uff1a{Path.GetFileName(_batchFiles[_batchIndex])}"
                    : $"\u7b2c {_batchIndex + 1} \u5f20\uff0c\u5171 {_batchFiles.Count} \u5f20\uff1a{Path.GetFileName(_batchFiles[_batchIndex])}";
            }
            else
            {
                BatchBar.Visibility = Visibility.Collapsed;
            }
        }

        private void BuildViewerFileList(string currentPath)
        {
            _viewerFiles.Clear();
            _viewerIndex = -1;

            try
            {
                string dir = Path.GetDirectoryName(currentPath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

                // 相机、手机导出的连续照片通常保留修改时间；按时间比按杂乱的文件名更接近用户眼中的“相邻照片”。
                var files = Directory.EnumerateFiles(dir)
                    .Where(IsSupportedImagePath)
                    .OrderBy(path => GetFileLastWriteTimeUtc(path))
                    .ThenBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                SetViewerFiles(files, currentPath);
            }
            catch
            {
                _viewerFiles.Clear();
                _viewerIndex = -1;
            }
        }

        private void SetViewerFiles(IEnumerable<string> files, string currentPath)
        {
            _viewerFiles.Clear();
            _viewerFiles.AddRange(files.Where(File.Exists));
            _viewerIndex = _viewerFiles.FindIndex(path => string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsSupportedImagePath(string path) =>
            Array.IndexOf(SupportedExtensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;

        private static DateTime GetFileLastWriteTimeUtc(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private void ViewerPrevButton_Click(object sender, RoutedEventArgs e) => GoToViewerIndex(_viewerIndex - 1);
        private void ViewerNextButton_Click(object sender, RoutedEventArgs e) => GoToViewerIndex(_viewerIndex + 1);

        private void GoToViewerIndex(int newIndex)
        {
            if (newIndex < 0 || newIndex >= _viewerFiles.Count) return;
            if (newIndex == _viewerIndex) return;

            if (_hasUnsavedChanges && !ConfirmDiscardUnsavedChanges("切换图片")) return;

            string path = _viewerFiles[newIndex];
            _viewerIndex = newIndex;
            ClearBatch();
            LoadImage(path, false);
            UpdateViewerNavigation();
        }

        private void UpdateViewerNavigation()
        {
            bool canNavigate = Canvas1.Image != null && !_editMode && _viewerFiles.Count > 1 && _viewerIndex >= 0;
            if (!canNavigate)
            {
                ViewerPrevButton.Visibility = Visibility.Collapsed;
                ViewerNextButton.Visibility = Visibility.Collapsed;
                ViewerBottomOverlay.Visibility = _editMode && Canvas1.Image != null ? Visibility.Visible : Visibility.Collapsed;
                UpdateTitleFileInfoText();
                return;
            }

            ViewerPrevButton.IsEnabled = _viewerIndex > 0;
            ViewerNextButton.IsEnabled = _viewerIndex < _viewerFiles.Count - 1;
            ViewerPrevButton.Opacity = ViewerPrevButton.IsEnabled ? 1 : 0.35;
            ViewerNextButton.Opacity = ViewerNextButton.IsEnabled ? 1 : 0.35;
            ApplyViewerHotZones();
            UpdateTitleFileInfoText();
        }

        private void ImageStage_MouseMove(object sender, MouseEventArgs e)
        {
            if (_editMode || Canvas1.Image == null)
            {
                SetViewerHotZones(false, false, false);
                return;
            }

            Point p = e.GetPosition(ImageStage);
            double width = Math.Max(ImageStage.ActualWidth, 1);
            double height = Math.Max(ImageStage.ActualHeight, 1);
            bool bottom = p.Y >= height - 96;
            bool left = p.X <= 112;
            bool right = p.X >= width - 112;
            SetViewerHotZones(bottom, left, right);
        }

        private void ImageStage_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_editMode) SetViewerHotZones(false, false, false);
        }

        private void SetViewerHotZones(bool bottom, bool left, bool right)
        {
            _viewerBottomHot = bottom;
            _viewerLeftHot = left;
            _viewerRightHot = right;
            ApplyViewerHotZones();
        }

        private void ApplyViewerHotZones()
        {
            if (_editMode || Canvas1.Image == null)
            {
                ViewerBottomOverlay.Visibility = _editMode && Canvas1.Image != null ? Visibility.Visible : Visibility.Collapsed;
                ViewerPrevButton.Visibility = Visibility.Collapsed;
                ViewerNextButton.Visibility = Visibility.Collapsed;
                return;
            }

            bool showBottomOverlay = _viewerBottomHot || _panMode;
            bool bottomOverlayWasVisible = ViewerBottomOverlay.Visibility == Visibility.Visible;
            ViewerBottomOverlay.Visibility = showBottomOverlay ? Visibility.Visible : Visibility.Collapsed;
            if (showBottomOverlay && !bottomOverlayWasVisible)
            {
                // Collapsed 状态下 ActualWidth 为 0；等布局完成再计算，避免右侧信息块被裁去左边框。
                Dispatcher.BeginInvoke(new Action(UpdateBottomOverlayConstraints), DispatcherPriority.Loaded);
            }
            bool hasList = _viewerFiles.Count > 1 && _viewerIndex >= 0;
            // 翻页是查看器的主操作，不再要求鼠标先碰到窗口边缘才出现。
            ViewerPrevButton.Visibility = hasList ? Visibility.Visible : Visibility.Collapsed;
            ViewerNextButton.Visibility = hasList ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadImage(string path, bool updateViewerList = true)
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
            _currentProjectPath = null;
            _currentExtension = ext == ".webp" ? ".png" : ext;
            _hasUnsavedChanges = false;
            SetCurrentFileName(Path.GetFileName(path));
            UpdateImageInfo(bmp, TryGetFileSize(path));
            AddRecentFile(path);
            if (updateViewerList) BuildViewerFileList(path);
            SetEditMode(false, false);
            FitImageAfterLayout();
            UpdateStatus("图片已打开");
            UpdateViewerNavigation();
        }

        private void LoadProject(string path)
        {
            PicMarkProject project;
            try
            {
                project = ProjectStore.Load(path);
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, $"无法打开这个 PicMark 项目：{ex.Message}", "提示");
                return;
            }

            _loadingDocument = true;
            try
            {
                Canvas1.Image = project.Image;
                Canvas1.LoadState(project.Annotations, project.Watermark);
            }
            finally
            {
                _loadingDocument = false;
            }

            SetImageWorkspaceVisible(true);
            _currentProjectPath = path;
            _currentFilePath = null;
            _currentExtension = ".png";
            _hasUnsavedChanges = false;
            SetCurrentFileName(Path.GetFileName(path));
            UpdateImageInfo(project.Image, TryGetFileSize(path));
            AddRecentFile(path);
            _viewerFiles.Clear();
            _viewerIndex = -1;
            SetEditMode(false, false);
            FitImageAfterLayout();
            UpdateStatus("项目已打开");
            UpdateViewerNavigation();
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
            try
            {
                if (!Clipboard.ContainsImage())
                {
                    UpdateStatus("剪贴板里没有图片");
                    return;
                }
                var src = Clipboard.GetImage();
                if (src == null)
                {
                    UpdateStatus("剪贴板图片格式暂不支持");
                    return;
                }
                if (!ConfirmDiscardUnsavedChanges("粘贴新图片")) return;
                if (src.CanFreeze) src.Freeze();

                ClearBatch();
                Canvas1.Image = src;
                Canvas1.ClearAll();
                SetImageWorkspaceVisible(true);
                _currentFilePath = null;
                _currentProjectPath = null;
                _currentExtension = ".png";
                _hasUnsavedChanges = false;
                SetCurrentFileName("剪贴板图片.png");
                UpdateImageInfo(src, null);
                _viewerFiles.Clear();
                _viewerIndex = -1;
                SetEditMode(false, false);
                FitImageAfterLayout();
                UpdateStatus("已粘贴剪贴板图片，保存时请选择保存位置");
                UpdateViewerNavigation();
            }
            catch (COMException ex)
            {
                UpdateStatus("读取剪贴板失败");
                AppDialog.Show(this, $"读取剪贴板图片失败：{ex.Message}", "粘贴失败");
            }
            catch (ExternalException ex)
            {
                UpdateStatus("读取剪贴板失败");
                AppDialog.Show(this, $"读取剪贴板图片失败：{ex.Message}", "粘贴失败");
            }
            catch (Exception ex)
            {
                UpdateStatus("读取剪贴板失败");
                AppDialog.Show(this, $"读取剪贴板图片失败：{ex.Message}", "粘贴失败");
            }
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
            if (BtnEditMode != null) BtnEditMode.IsEnabled = hasImage;
            if (BtnRotateTop != null) BtnRotateTop.IsEnabled = hasImage;
            if (!hasImage) SetEditMode(false, false);
        }

        private void UpdateZoomControls()
        {
            if (ZoomCombo == null) return;
            _updatingZoomCombo = true;
            ZoomCombo.Text = $"{Math.Round(Canvas1.Scale * 100)}%";
            _updatingZoomCombo = false;
            UpdateTitleFileInfoText();
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

        private void BtnEditMode_Click(object sender, RoutedEventArgs e)
        {
            SetEditMode(!_editMode, true);
        }

        private void ApplyChromeTheme(bool editMode)
        {
            SetResourceColor("TextPrimaryBrush", editMode ? Color.FromRgb(0xF4, 0xF4, 0xF5) : Color.FromRgb(0x1F, 0x2A, 0x37));
            SetResourceColor("TextSecondaryBrush", editMode ? Color.FromRgb(0xB9, 0xBE, 0xC7) : Color.FromRgb(0x64, 0x74, 0x8A));
            SetResourceColor("PanelBrush", editMode ? Color.FromRgb(0x34, 0x34, 0x34) : Color.FromRgb(0xFF, 0xFF, 0xFF));
            SetResourceColor("PanelToolSurfaceBrush", editMode ? Color.FromRgb(0x3A, 0x3A, 0x3A) : Color.FromRgb(0xF8, 0xFA, 0xFC));
            SetResourceColor("PanelToolBorderBrush", editMode ? Color.FromRgb(0x50, 0x50, 0x50) : Color.FromRgb(0xE1, 0xE8, 0xF0));
            SetResourceColor("ToolbarBrush", editMode ? Color.FromRgb(0x25, 0x25, 0x25) : Color.FromRgb(0xFF, 0xFF, 0xFF));
            SetResourceColor("TitleBarBrush", editMode ? Color.FromRgb(0x1C, 0x1C, 0x1C) : Color.FromRgb(0xF8, 0xFA, 0xFC));
            SetResourceColor("BorderBrush1", editMode ? Color.FromRgb(0x46, 0x46, 0x46) : Color.FromRgb(0xE1, 0xE8, 0xF0));
            SetResourceColor("HoverBrush", editMode ? Color.FromRgb(0x3F, 0x3F, 0x3F) : Color.FromRgb(0xF1, 0xF5, 0xF9));
            SetResourceColor("PressedBrush", editMode ? Color.FromRgb(0x4A, 0x4A, 0x4A) : Color.FromRgb(0xE8, 0xED, 0xF4));
            SetResourceColor("TitleBarControlHoverBrush", editMode ? Color.FromRgb(0x3F, 0x3F, 0x3F) : Color.FromRgb(0xEA, 0xEF, 0xF5));
            SetResourceColor("TitleBarControlPressedBrush", editMode ? Color.FromRgb(0x4A, 0x4A, 0x4A) : Color.FromRgb(0xE1, 0xE8, 0xF0));
            SetResourceColor("TitleUpdateHoverBrush", editMode ? Color.FromRgb(0x30, 0x3A, 0x48) : Color.FromRgb(0xF1, 0xF3, 0xFF));
            SetResourceColor("TitleUpdatePressedBrush", editMode ? Color.FromRgb(0x25, 0x2D, 0x38) : Color.FromRgb(0xE8, 0xEB, 0xFF));

            var workspace = editMode ? BrushFromRgb(0x24, 0x24, 0x24) : BrushFromRgb(0xF4, 0xF6, 0xF8);
            Background = workspace;
            MainWorkspace.Background = workspace;
            ImageStage.Background = workspace;
            Scroller.Background = workspace;
            TitleBar.BorderBrush = editMode ? BrushFromRgb(0x38, 0x38, 0x38) : BrushFromRgb(0xE1, 0xE8, 0xF0);
            TopToolbar.BorderBrush = editMode ? BrushFromRgb(0x3A, 0x3A, 0x3A) : BrushFromRgb(0xE1, 0xE8, 0xF0);
            ApplyWindowFrame(editMode);
            SetFloatingBadgeTheme(
                editMode ? BrushFromArgb(0xF5, 0x30, 0x30, 0x30) : BrushFromArgb(0xF8, 0xFF, 0xFF, 0xFF),
                editMode ? BrushFromRgb(0x4A, 0x4A, 0x4A) : BrushFromRgb(0xE1, 0xE8, 0xF0),
                editMode ? BrushFromRgb(0xF4, 0xF4, 0xF5) : BrushFromRgb(0x33, 0x41, 0x55));
            ApplyTitleFileInfoTheme(editMode);

            if (CanvasShadow != null && !CanvasShadow.IsFrozen)
            {
                CanvasShadow.BlurRadius = editMode ? 28 : 22;
                CanvasShadow.ShadowDepth = editMode ? 8 : 5;
                CanvasShadow.Opacity = editMode ? 0.45 : 0.14;
            }

            ApplyZoomComboTheme(editMode);
            ApplyPopupTheme(editMode);
            ApplyContextMenuTheme(editMode);
            ApplyTopToolbarMode(editMode);
            ApplyButtonPalette(editMode);
            ApplyWindowButtonPalette(editMode);
            ApplyEditorPanelTheme(editMode);
            RefreshToolButtonStates();
            UpdateTitleUpdateUi();
        }

        private void ApplyEditorPanelTheme(bool editMode)
        {
            Brush panel = editMode ? BrushFromRgb(0x34, 0x34, 0x34) : Brushes.White;
            Brush border = editMode ? BrushFromRgb(0x46, 0x46, 0x46) : BrushFromRgb(0xE1, 0xE8, 0xF0);
            if (RightEditPanel != null)
            {
                RightEditPanel.Background = panel;
                RightEditPanel.BorderBrush = border;
            }
            if (CropPanel != null)
            {
                CropPanel.Background = editMode ? BrushFromRgb(0x32, 0x32, 0x32) : Brushes.White;
                CropPanel.BorderBrush = editMode ? BrushFromRgb(0x50, 0x50, 0x50) : border;
            }
            if (WatermarkPanel != null)
            {
                WatermarkPanel.Background = editMode ? BrushFromRgb(0x32, 0x32, 0x32) : Brushes.White;
                WatermarkPanel.BorderBrush = editMode ? BrushFromRgb(0x50, 0x50, 0x50) : border;
            }
            if (WatermarkTextBox != null)
            {
                WatermarkTextBox.Background = editMode ? BrushFromRgb(0x26, 0x26, 0x26) : Brushes.White;
                WatermarkTextBox.Foreground = editMode ? BrushFromRgb(0xF4, 0xF4, 0xF5) : BrushFromRgb(0x1F, 0x2A, 0x37);
                WatermarkTextBox.BorderBrush = editMode ? BrushFromRgb(0x50, 0x50, 0x50) : BrushFromRgb(0xDC, 0xE4, 0xEC);
            }
        }
        private void ApplyTitleFileInfoTheme(bool editMode)
        {
            if (TitleFileInfoText == null) return;
            TitleFileInfoText.Visibility = Visibility.Visible;
            TitleFileInfoText.Foreground = editMode
                ? BrushFromRgb(0xF4, 0xF4, 0xF5)
                : BrushFromRgb(0x18, 0x22, 0x30);

            if (TitleVersionText != null)
                TitleVersionText.Foreground = editMode
                    ? BrushFromRgb(0x9B, 0xA6, 0xB7)
                    : BrushFromRgb(0x72, 0x80, 0x91);
        }

        private void ApplyWindowFrame(bool editMode)
        {
            if (WindowShell == null) return;

            var chrome = WindowChrome.GetWindowChrome(this);
            bool maximized = WindowState == WindowState.Maximized;

            if (maximized)
            {
                WindowShell.Margin = new Thickness(6);
                WindowShell.CornerRadius = new CornerRadius(0);
                WindowShell.BorderThickness = new Thickness(0);

                // WindowChrome 最大化时窗口可能覆盖整个屏幕（含任务栏区域），手动留出任务栏空间
                var work = SystemParameters.WorkArea;
                double padTop = Math.Max(0, work.Top);
                double padBottom = Math.Max(0, SystemParameters.PrimaryScreenHeight - work.Bottom);
                double padLeft = Math.Max(0, work.Left);
                double padRight = Math.Max(0, SystemParameters.PrimaryScreenWidth - work.Right);
                WindowShell.Padding = new Thickness(padLeft, padTop, padRight, padBottom);

                if (chrome != null)
                    chrome.ResizeBorderThickness = new Thickness(0);
            }
            else
            {
                WindowShell.Margin = new Thickness(0);
                WindowShell.CornerRadius = new CornerRadius(12);
                WindowShell.BorderThickness = new Thickness(1);
                WindowShell.Padding = new Thickness(0);

                if (chrome != null)
                    chrome.ResizeBorderThickness = new Thickness(4);
            }

            WindowShell.Background = editMode ? BrushFromRgb(0x1C, 0x1C, 0x1C) : BrushFromRgb(0xF2, 0xF6, 0xF8);
            WindowShell.BorderBrush = editMode ? BrushFromRgb(0x38, 0x38, 0x38) : BrushFromRgb(0xB8, 0xC9, 0xD6);
        }

        private void SetFloatingBadgeTheme(Brush background, Brush border, Brush foreground)
        {
            if (BottomZoomBar != null)
            {
                BottomZoomBar.Background = background;
                BottomZoomBar.BorderBrush = border;
            }
            if (ImageInfoBadge != null)
            {
                ImageInfoBadge.Background = background;
                ImageInfoBadge.BorderBrush = border;
            }
            if (CurrentFileBadge != null)
            {
                CurrentFileBadge.Background = background;
                CurrentFileBadge.BorderBrush = border;
            }
            if (ImageInfoText != null) ImageInfoText.Foreground = foreground;
            if (CurrentFileText != null) CurrentFileText.Foreground = foreground;
        }

        private void ApplyZoomComboTheme(bool editMode)
        {
            if (ZoomCombo == null) return;

            if (editMode)
            {
                var background = BrushFromRgb(0x30, 0x30, 0x30);
                var foreground = BrushFromRgb(0xF4, 0xF4, 0xF5);
                ApplyComboStyle(ZoomCombo, (Style)FindResource("DarkComboBox"));
                ZoomCombo.Background = background;
                ZoomCombo.BorderBrush = BrushFromRgb(0x4A, 0x4A, 0x4A);
                ZoomCombo.Foreground = foreground;
                ApplyZoomComboItemTheme(background, foreground);
                ApplyZoomComboTextTheme(foreground);
            }
            else
            {
                var background = BrushFromRgb(0xFF, 0xFF, 0xFF);
                var foreground = BrushFromRgb(0x2B, 0x37, 0x48);
                ApplyComboStyle(ZoomCombo, (Style)FindResource("ViewerComboBox"));
                ZoomCombo.Background = background;
                ZoomCombo.BorderBrush = BrushFromRgb(0xC9, 0xD7, 0xE7);
                ZoomCombo.Foreground = foreground;
                ApplyZoomComboItemTheme(background, foreground);
                ApplyZoomComboTextTheme(foreground);
            }
        }

        private void ApplyZoomComboItemTheme(Brush background, Brush foreground)
        {
            if (ZoomCombo == null) return;
            foreach (var item in ZoomCombo.Items.OfType<ComboBoxItem>())
            {
                item.Background = background;
                item.Foreground = foreground;
            }
        }

        private void ApplyZoomComboTextTheme(Brush foreground)
        {
            if (ZoomCombo == null) return;
            ZoomCombo.ApplyTemplate();
            if (ZoomCombo.Template.FindName("PART_EditableTextBox", ZoomCombo) is TextBox textBox)
                textBox.Foreground = foreground;
        }

        private static void ApplyComboStyle(ComboBox comboBox, Style style)
        {
            if (comboBox != null && style != null && !ReferenceEquals(comboBox.Style, style))
                comboBox.Style = style;
        }

        private void ApplyPopupTheme(bool editMode)
        {
            Brush background = editMode ? BrushFromRgb(0x30, 0x30, 0x30) : BrushFromRgb(0xF7, 0xFA, 0xFB);
            Brush border = editMode ? BrushFromRgb(0x4A, 0x4A, 0x4A) : BrushFromRgb(0xB9, 0xCB, 0xD8);
            Brush foreground = editMode ? BrushFromRgb(0xF4, 0xF4, 0xF5) : BrushFromRgb(0x26, 0x32, 0x41);

            if (SavePopupPanel != null)
            {
                SavePopupPanel.Background = background;
                SavePopupPanel.BorderBrush = border;
                ApplyPopupContentTheme(SavePopupPanel, editMode, foreground);
            }
            if (OptionsPopupPanel != null)
            {
                OptionsPopupPanel.Background = background;
                OptionsPopupPanel.BorderBrush = border;
                ApplyPopupContentTheme(OptionsPopupPanel, editMode, foreground);
            }
            if (RotatePopupPanel != null)
            {
                RotatePopupPanel.Background = background;
                RotatePopupPanel.BorderBrush = border;
                ApplyPopupContentTheme(RotatePopupPanel, editMode, foreground);
            }
        }

        private void ApplyPopupContentTheme(DependencyObject root, bool editMode, Brush foreground)
        {
            if (root == null) return;

            if (root is Button button)
            {
                ApplyButtonStyle(button, (Style)FindResource(editMode ? "SaveMenuButton" : "ViewerMenuButton"));
                button.Foreground = foreground;
                button.Background = Brushes.Transparent;
                button.BorderBrush = Brushes.Transparent;
                button.BorderThickness = new Thickness(0);
            }
            else if (root is TextBlock textBlock)
            {
                textBlock.Foreground = foreground;
            }

            int visualChildren = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < visualChildren; i++)
                ApplyPopupContentTheme(VisualTreeHelper.GetChild(root, i), editMode, foreground);

            foreach (object logicalChild in LogicalTreeHelper.GetChildren(root))
            {
                if (logicalChild is DependencyObject child)
                ApplyPopupContentTheme(child, editMode, foreground);
            }
        }

        private void ApplyContextMenuTheme(bool editMode)
        {
            if (ImageContextMenu == null) return;

            Brush background = editMode ? BrushFromRgb(0x30, 0x30, 0x30) : BrushFromRgb(0xF7, 0xFA, 0xFB);
            Brush border = editMode ? BrushFromRgb(0x4A, 0x4A, 0x4A) : BrushFromRgb(0xB9, 0xCB, 0xD8);
            Brush foreground = editMode ? BrushFromRgb(0xF4, 0xF4, 0xF5) : BrushFromRgb(0x26, 0x32, 0x41);
            Brush hover = editMode ? BrushFromRgb(0x3F, 0x3F, 0x3F) : BrushFromRgb(0xE2, 0xEC, 0xF2);
            Brush disabled = editMode ? BrushFromRgb(0x7A, 0x7F, 0x89) : BrushFromRgb(0x78, 0x86, 0x96);

            ImageContextMenu.Background = background;
            ImageContextMenu.BorderBrush = border;
            ImageContextMenu.Foreground = foreground;
            ImageContextMenu.Resources[SystemColors.MenuBrushKey] = background;
            ImageContextMenu.Resources[SystemColors.MenuBarBrushKey] = background;
            ImageContextMenu.Resources[SystemColors.MenuTextBrushKey] = foreground;
            ImageContextMenu.Resources[SystemColors.ControlBrushKey] = background;
            ImageContextMenu.Resources[SystemColors.ControlLightBrushKey] = background;
            ImageContextMenu.Resources[SystemColors.ControlLightLightBrushKey] = background;
            ImageContextMenu.Resources[SystemColors.ControlTextBrushKey] = foreground;
            ImageContextMenu.Resources[SystemColors.WindowBrushKey] = background;
            ImageContextMenu.Resources[SystemColors.WindowTextBrushKey] = foreground;
            ImageContextMenu.Resources[SystemColors.HighlightBrushKey] = hover;
            ImageContextMenu.Resources[SystemColors.HighlightTextBrushKey] = foreground;
            ImageContextMenu.Resources[SystemColors.MenuHighlightBrushKey] = hover;
            ImageContextMenu.Resources[SystemColors.GrayTextBrushKey] = disabled;
            ApplyContextMenuItemTheme(ImageContextMenu, background, foreground, hover, disabled);
        }

        private void ApplyContextMenuItemTheme(ItemsControl root, Brush background, Brush foreground, Brush hover, Brush disabled)
        {
            if (root == null) return;
            foreach (object item in root.Items)
            {
                if (item is MenuItem menuItem)
                {
                    menuItem.Foreground = foreground;
                    menuItem.Resources[SystemColors.MenuBrushKey] = background;
                    menuItem.Resources[SystemColors.MenuBarBrushKey] = background;
                    menuItem.Resources[SystemColors.MenuTextBrushKey] = foreground;
                    menuItem.Resources[SystemColors.ControlBrushKey] = background;
                    menuItem.Resources[SystemColors.ControlLightBrushKey] = background;
                    menuItem.Resources[SystemColors.ControlLightLightBrushKey] = background;
                    menuItem.Resources[SystemColors.ControlTextBrushKey] = foreground;
                    menuItem.Resources[SystemColors.WindowBrushKey] = background;
                    menuItem.Resources[SystemColors.WindowTextBrushKey] = foreground;
                    menuItem.Resources[SystemColors.HighlightBrushKey] = hover;
                    menuItem.Resources[SystemColors.HighlightTextBrushKey] = foreground;
                    menuItem.Resources[SystemColors.MenuHighlightBrushKey] = hover;
                    menuItem.Resources[SystemColors.GrayTextBrushKey] = disabled;
                    if (menuItem.HasItems)
                        ApplyContextMenuItemTheme(menuItem, background, foreground, hover, disabled);
                }
            }
        }

        private void ApplyTopToolbarMode(bool editMode)
        {
            Visibility editorVisibility = editMode ? Visibility.Visible : Visibility.Collapsed;
            if (BtnPrintTop != null) BtnPrintTop.Visibility = editorVisibility;
            if (BtnCompressTop != null) BtnCompressTop.Visibility = Visibility.Collapsed;
            if (BtnRotateTop != null) BtnRotateTop.Visibility = editorVisibility;
            if (BtnUndoTop != null) BtnUndoTop.Visibility = editorVisibility;
            if (BtnRedoTop != null) BtnRedoTop.Visibility = editorVisibility;
            if (BtnSave != null) BtnSave.Visibility = editorVisibility;
        }

        private void ApplyButtonPalette(bool editMode)
        {
            Style primaryStyle = (Style)FindResource(editMode ? "PrimaryButton" : "ViewerPrimaryButton");
            Style toolStyle = (Style)FindResource(editMode ? "ToolButton" : "ViewerToolButton");
            Style bottomStyle = (Style)FindResource(editMode ? "BottomBarButton" : "ViewerBottomBarButton");
            Style navStyle = (Style)FindResource(editMode ? "ToolButton" : "ViewerNavButton");
            Brush normalForeground = editMode ? BrushFromRgb(0xF4, 0xF4, 0xF5) : BrushFromRgb(0x25, 0x31, 0x42);
            Brush topButtonBackground = editMode ? BrushFromRgb(0x30, 0x30, 0x30) : BrushFromRgb(0xF7, 0xFA, 0xFB);
            Brush topButtonBorder = editMode ? BrushFromRgb(0x4A, 0x4A, 0x4A) : BrushFromRgb(0xC8, 0xD6, 0xE2);
            Brush transparent = Brushes.Transparent;

            foreach (var button in new[] { BtnSave, BtnEditMode })
                ApplyButtonStyle(button, primaryStyle);
            bool hasImage = Canvas1.Image != null;
            ApplyButtonStyle(BtnOpen, hasImage ? toolStyle : primaryStyle);

            foreach (var button in new[]
            {
                BtnOptions, BtnPrintTop, BtnCompressTop, BtnRotateTop, BtnUndoTop, BtnRedoTop
            })
            {
                ApplyButtonStyle(button, toolStyle);
                ResetNeutralButton(button, normalForeground, topButtonBackground, topButtonBorder, new Thickness(1));
            }

            foreach (var button in new[] { ViewerPrevButton, ViewerNextButton })
            {
                ApplyButtonStyle(button, navStyle);
                if (editMode)
                    ResetNeutralButton(button, normalForeground, transparent, transparent, new Thickness(0));
                else
                    ResetNeutralButton(button, BrushFromRgb(0x40, 0x56, 0x6B), Brushes.Transparent, Brushes.Transparent, new Thickness(0));
            }

            foreach (var button in new[] { BottomZoomInBtn, BottomZoomOutBtn, BottomZoomFitBtn, BottomZoomResetBtn })
            {
                ApplyButtonStyle(button, bottomStyle);
                ResetNeutralButton(button, normalForeground, transparent, transparent, new Thickness(0));
            }

            ResetBottomPanButton();
            if (BtnEditMode != null)
            {
                BtnEditMode.Background = (Brush)FindResource("AccentBrush");
                BtnEditMode.Foreground = Brushes.White;
                BtnEditMode.BorderBrush = (Brush)FindResource("AccentBrush");
                BtnEditMode.BorderThickness = new Thickness(0);
            }

            if (BtnOpen != null)
            {
                if (hasImage)
                    ResetNeutralButton(BtnOpen, normalForeground, topButtonBackground, topButtonBorder, new Thickness(1));
                else
                {
                    BtnOpen.Background = (Brush)FindResource("AccentBrush");
                    BtnOpen.Foreground = Brushes.White;
                    BtnOpen.BorderBrush = (Brush)FindResource("AccentBrush");
                    BtnOpen.BorderThickness = new Thickness(0);
                }
            }
            if (BtnSave != null)
            {
                BtnSave.Background = (Brush)FindResource("AccentBrush");
                BtnSave.Foreground = Brushes.White;
                BtnSave.BorderBrush = (Brush)FindResource("AccentBrush");
                BtnSave.BorderThickness = new Thickness(0);
            }
        }

        private void ApplyWindowButtonPalette(bool editMode)
        {
            Brush iconBrush = editMode ? BrushFromRgb(0xF4, 0xF4, 0xF5) : BrushFromRgb(0x36, 0x45, 0x56);
            foreach (var path in new[] { MaximizeIcon, RestoreIcon })
                if (path != null) path.Stroke = iconBrush;

            if (BtnMinimize?.Content is System.Windows.Shapes.Path minimizePath)
                minimizePath.Stroke = iconBrush;
            if (BtnClose?.Content is System.Windows.Shapes.Path closePath)
                closePath.Stroke = iconBrush;

            if (BtnMinimize != null) BtnMinimize.Foreground = iconBrush;
            if (BtnMaximize != null) BtnMaximize.Foreground = iconBrush;
            if (BtnClose != null) BtnClose.Foreground = iconBrush;
        }

        private static void ApplyButtonStyle(Button button, Style style)
        {
            if (button != null && style != null && !ReferenceEquals(button.Style, style))
                button.Style = style;
        }

        private static void ResetNeutralButton(Button button, Brush foreground, Brush background, Brush border, Thickness borderThickness)
        {
            if (button == null) return;
            button.Foreground = foreground;
            button.Background = background;
            button.BorderBrush = border;
            button.BorderThickness = borderThickness;
        }

        private void ResetBottomPanButton()
        {
            if (BottomPanBtn == null) return;

            if (_panMode)
            {
                ApplyButtonStyle(BottomPanBtn, (Style)FindResource(_editMode ? "BottomBarButton" : "ViewerBottomActiveButton"));
                if (_editMode)
                {
                    BottomPanBtn.Background = (Brush)FindResource("ActiveBrush");
                    BottomPanBtn.Foreground = Brushes.White;
                    BottomPanBtn.BorderBrush = (Brush)FindResource("ActiveBorderBrush");
                    BottomPanBtn.BorderThickness = new Thickness(2);
                }
                else
                {
                    BottomPanBtn.Background = BrushFromArgb(0xD9, 0x26, 0x32, 0x41);
                    BottomPanBtn.Foreground = Brushes.White;
                    BottomPanBtn.BorderBrush = BrushFromArgb(0x66, 0x3F, 0x4B, 0x5D);
                    BottomPanBtn.BorderThickness = new Thickness(1);
                }
                return;
            }

            ApplyButtonStyle(BottomPanBtn, (Style)FindResource(_editMode ? "BottomBarButton" : "ViewerBottomBarButton"));
            if (_editMode)
            {
                BottomPanBtn.Background = Brushes.Transparent;
                BottomPanBtn.Foreground = (Brush)FindResource("TextPrimaryBrush");
                BottomPanBtn.BorderBrush = Brushes.Transparent;
                BottomPanBtn.BorderThickness = new Thickness(0);
            }
            else
            {
                BottomPanBtn.Background = BrushFromRgb(0xE7, 0xF0, 0xF6);
                BottomPanBtn.Foreground = BrushFromRgb(0x4B, 0x5B, 0x6D);
                BottomPanBtn.BorderBrush = BrushFromRgb(0xC3, 0xD3, 0xDF);
                BottomPanBtn.BorderThickness = new Thickness(1);
            }
        }

        private void SetResourceColor(string key, Color color)
        {
            if (TryFindResource(key) is SolidColorBrush brush && !brush.IsFrozen)
            {
                brush.Color = color;
                return;
            }

            // Styles may freeze a brush resolved with StaticResource. Replacing the
            // resource keeps DynamicResource consumers in sync with the active theme.
            Resources[key] = new SolidColorBrush(color);
        }

        private static SolidColorBrush BrushFromRgb(byte r, byte g, byte b) =>
            new SolidColorBrush(Color.FromRgb(r, g, b));

        private static SolidColorBrush BrushFromArgb(byte a, byte r, byte g, byte b) =>
            new SolidColorBrush(Color.FromArgb(a, r, g, b));

        private void SetEditMode(bool enabled, bool showStatus)
        {
            bool next = enabled && Canvas1.Image != null;
            _editMode = next;
            Canvas1.IsEditingEnabled = next;
            ApplyChromeTheme(next);

            if (RightEditColumn != null)
                RightEditColumn.Width = next ? new GridLength(RightEditPanelWidth) : new GridLength(0);
            if (RightEditPanel != null)
                RightEditPanel.Visibility = next ? Visibility.Visible : Visibility.Collapsed;
            if (BtnEditModeText != null)
                BtnEditModeText.Text = next ? "查看" : "编辑";
            if (BtnEditMode != null)
                BtnEditMode.ToolTip = next ? "切回查看 (Ctrl+E)" : "进入编辑工具 (Ctrl+E)";

            if (!next)
            {
                ArrowPopup.IsOpen = false;
                ShapeHintPopup.IsOpen = false;
                WatermarkPanel.Visibility = Visibility.Collapsed;
                CropPanel.Visibility = Visibility.Collapsed;
                if (Canvas1.CurrentTool == AnnotationTool.Crop) Canvas1.CancelCrop();
                Canvas1.CurrentTool = AnnotationTool.Select;
                _panMode = false;
                _isPanningCanvas = false;
                Scroller.Cursor = null;
                ResetBottomPanButton();
            }

            UpdateAnnotationPropertiesPanel();
            FitImageAfterLayout();
            UpdateViewerNavigation();
            if (showStatus)
                UpdateStatus(next ? "编辑工具已打开" : "已切回查看");
        }

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
                SetToolButtonState(new[] { PanelBtnRect, PanelBtnEllipse, PanelBtnArrow, PanelBtnFreehand, PanelBtnMosaic, PanelBtnText }, "Pan", null, true);
                ResetBottomPanButton();
                UpdateViewerNavigation();
                if (Scroller.ScrollableWidth <= 0 && Scroller.ScrollableHeight <= 0)
                    UpdateStatus("抓手已开启；当前图片完整显示，放大后按住图片拖动更明显。");
                else
                    UpdateStatus("抓手已开启；按住图片区域拖动查看。");
            }
            else
            {
                Scroller.Cursor = null;
                SetActiveTool("Select");
                UpdateViewerNavigation();
                UpdateStatus("已切回选择/移动。");
            }
        }

        private void ToolButton_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var tag = (string)button.Tag;
            SetEditMode(true, false);
            SetActiveTool(tag);
            if (tag == "Select")
                UpdateStatus("选择 / 移动：单击标注可选中，拖拽可移动。");
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
                UpdateStatus("马赛克：画完立即出现缩放抓手和旋转箭头；拖动时按当前位置实时预览。");
            }
            if (tag == "Text")
                UpdateStatus("点击图片上的位置，然后直接输入文字；按 Enter 完成。");
            if (tag == "Crop")
                UpdateStatus("拖动边角或边线调整裁剪范围，按 Enter 确认，按 Esc 取消。");
        }

        private void BtnWatermark_Click(object sender, RoutedEventArgs e)
        {
            if (Canvas1.Image == null)
            {
                AppDialog.Show(this, "请先打开一张图片，再添加水印。", "提示");
                return;
            }

            SetEditMode(true, false);
            WatermarkPanel.Visibility = Visibility.Visible;
            SetActiveTool("Select");
            UpdateAnnotationPropertiesPanel();
            if (Canvas1.GetWatermark() == null)
            {
                ApplyWatermarkPreset(_settings.LastWatermarkPreset);
                UpdateStatus("\u5df2\u5e94\u7528\u4e0a\u6b21\u7684\u6c34\u5370\u8bbe\u7f6e\uff0c\u53ef\u5728\u53f3\u4fa7\u7ee7\u7eed\u8c03\u6574\u3002");
            }
            else
            {
                WatermarkParametersPanel.Visibility = Visibility.Visible;
                UpdateWatermarkControls();
                UpdateStatus("已打开水印设置，可继续调整当前水印。");
            }
        }

        private void BtnCloseWatermarkPanel_Click(object sender, RoutedEventArgs e)
        {
            WatermarkPanel.Visibility = Visibility.Collapsed;
            UpdateAnnotationPropertiesPanel();
        }

        private static string BuildCertificateWatermarkText() =>
            $"本证件/文件仅用于XX\n挪作他用无效 {DateTime.Now:yyyy年M月d日}";

        private static string NormalizeWatermarkTemplate(string template)
        {
            switch (template)
            {
                case "TiledText":
                case "SingleText":
                case "ImageLogo":
                case "CertificateGrid":
                    return template;
                default:
                    return "CertificateGrid";
            }
        }

        private void QueueWatermarkDefaultsSave()
        {
            if (_initializingWatermarkUi) return;
            _watermarkDefaultsSaveTimer.Stop();
            _watermarkDefaultsSaveTimer.Start();
        }

        private void WatermarkDefaultsSaveTimer_Tick(object sender, EventArgs e)
        {
            _watermarkDefaultsSaveTimer.Stop();
            SaveWatermarkDefaultsNow();
        }

        private static double ClampWatermarkSlider(Slider slider, double value)
        {
            return Math.Max(slider.Minimum, Math.Min(slider.Maximum, value));
        }

        private static string FormatWatermarkColor(Color color) =>
            $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

        private static Color ParseWatermarkColor(string value, Color fallback)
        {
            try
            {
                object converted = ColorConverter.ConvertFromString(value);
                return converted is Color color ? color : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private string GetCurrentWatermarkTemplate()
        {
            if (_watermarkStyle == WatermarkStyle.ImageLogo) return "ImageLogo";
            if (_watermarkLayout == WatermarkLayout.Single) return "SingleText";
            return _watermarkStyle == WatermarkStyle.TextOnly ? "TiledText" : "CertificateGrid";
        }

        private void ApplyWatermarkPresetToControls(WatermarkPreset source)
        {
            var preset = source?.Clone() ?? new WatermarkPreset();
            string template = NormalizeWatermarkTemplate(preset.Template);
            bool wasInitializing = _initializingWatermarkUi;
            _initializingWatermarkUi = true;
            switch (template)
            {
                case "TiledText":
                    _watermarkStyle = WatermarkStyle.TextOnly;
                    _watermarkLayout = WatermarkLayout.Tiled;
                    break;
                case "SingleText":
                    _watermarkStyle = WatermarkStyle.TextOnly;
                    _watermarkLayout = WatermarkLayout.Single;
                    break;
                case "ImageLogo":
                    _watermarkStyle = WatermarkStyle.ImageLogo;
                    _watermarkLayout = WatermarkLayout.Single;
                    break;
                default:
                    _watermarkStyle = WatermarkStyle.DiamondGrid;
                    _watermarkLayout = WatermarkLayout.Tiled;
                    break;
            }

            WatermarkTextBox.Text = string.IsNullOrWhiteSpace(preset.Text) && _watermarkStyle != WatermarkStyle.ImageLogo
                ? BuildCertificateWatermarkText()
                : preset.Text ?? string.Empty;
            _watermarkFontFamily = string.IsNullOrWhiteSpace(preset.FontFamily) ? "Microsoft YaHei UI" : preset.FontFamily;
            _watermarkBold = preset.Bold;
            _watermarkColor = ParseWatermarkColor(preset.Color, Colors.Black);
            _watermarkColorUserSelected = true;
            _watermarkLogoPath = preset.LogoPath ?? string.Empty;
            _watermarkLogoFlipHorizontal = preset.LogoFlipHorizontal;
            _watermarkLogoFlipVertical = preset.LogoFlipVertical;
            WatermarkOpacitySlider.Value = ClampWatermarkSlider(WatermarkOpacitySlider, preset.Opacity);
            WatermarkFontSizeSlider.Value = ClampWatermarkSlider(WatermarkFontSizeSlider, preset.FontSize);
            WatermarkAngleSlider.Value = ClampWatermarkSlider(WatermarkAngleSlider, preset.Angle);
            WatermarkSpacingSlider.Value = ClampWatermarkSlider(WatermarkSpacingSlider, preset.Spacing);
            WatermarkOffsetSlider.Value = ClampWatermarkSlider(WatermarkOffsetSlider, preset.HorizontalOffset);
            WatermarkLogoScaleSlider.Value = ClampWatermarkSlider(WatermarkLogoScaleSlider, preset.LogoScalePercent);
            _settings.WatermarkTemplate = template;
            _settings.WatermarkText = WatermarkTextBox.Text;
            _initializingWatermarkUi = wasInitializing;
            UpdateWatermarkControls();
        }

        private WatermarkPreset CreateCurrentWatermarkPreset()
        {
            var watermark = Canvas1?.GetWatermark();
            return new WatermarkPreset
            {
                Text = watermark?.Text ?? WatermarkTextBox?.Text ?? string.Empty,
                Template = watermark != null ? GetWatermarkTemplate(watermark) : GetCurrentWatermarkTemplate(),
                FontFamily = watermark?.FontFamilyName ?? _watermarkFontFamily,
                Bold = watermark?.Bold ?? _watermarkBold,
                Color = FormatWatermarkColor(watermark?.Color ?? _watermarkColor),
                Opacity = (watermark?.Opacity ?? (WatermarkOpacitySlider?.Value ?? 20) / 100.0) * 100.0,
                FontSize = watermark?.FontSize ?? WatermarkFontSizeSlider?.Value ?? 28,
                Angle = watermark?.Angle ?? WatermarkAngleSlider?.Value ?? 0,
                Spacing = watermark?.Spacing ?? WatermarkSpacingSlider?.Value ?? 204,
                HorizontalOffset = watermark?.HorizontalOffset ?? WatermarkOffsetSlider?.Value ?? 0,
                VerticalOffset = watermark?.VerticalOffset ?? 0,
                LogoPath = watermark?.LogoPath ?? _watermarkLogoPath,
                LogoScalePercent = watermark?.LogoScalePercent ?? WatermarkLogoScaleSlider?.Value ?? 18,
                LogoFlipHorizontal = watermark?.LogoFlipHorizontal ?? _watermarkLogoFlipHorizontal,
                LogoFlipVertical = watermark?.LogoFlipVertical ?? _watermarkLogoFlipVertical
            };
        }

        private void SaveWatermarkDefaultsNow()
        {
            if (WatermarkTextBox == null) return;
            var preset = CreateCurrentWatermarkPreset();
            _settings.LastWatermarkPreset = preset.Clone();
            _settings.WatermarkText = preset.Text;
            _settings.WatermarkTemplate = preset.Template;
            _settings.Save();
            UpdateWatermarkTextHistoryUi();
        }

        private static bool IsRecordableWatermarkPreset(WatermarkPreset preset)
        {
            if (preset == null) return false;
            return preset.Template == "ImageLogo"
                ? !string.IsNullOrWhiteSpace(preset.LogoPath)
                : !string.IsNullOrWhiteSpace(preset.Text);
        }

        private void SaveCurrentWatermarkPresetToHistory()
        {
            var preset = CreateCurrentWatermarkPreset();
            if (!IsRecordableWatermarkPreset(preset)) return;

            _settings.WatermarkPresetHistory.RemoveAll(item => item.IsEquivalentTo(preset));
            _settings.WatermarkPresetHistory.Insert(0, preset.Clone());
            while (_settings.WatermarkPresetHistory.Count > 12)
                _settings.WatermarkPresetHistory.RemoveAt(_settings.WatermarkPresetHistory.Count - 1);
            _settings.LastWatermarkPreset = preset.Clone();
            _settings.WatermarkText = preset.Text;
            _settings.WatermarkTemplate = preset.Template;
            _settings.Save();
            UpdateWatermarkTextHistoryUi();
        }

        private void ApplyWatermarkPreset(WatermarkPreset source)
        {
            var preset = source?.Clone() ?? new WatermarkPreset();
            ApplyWatermarkPresetToControls(preset);
            if (Canvas1.Image == null) return;

            _hasSelectedWatermarkTemplate = true;
            WatermarkParametersPanel.Visibility = Visibility.Visible;
            ApplyWatermarkFromControls();
            var watermark = Canvas1.GetWatermark();
            if (watermark != null)
            {
                watermark.VerticalOffset = preset.VerticalOffset;
                Canvas1.SetWatermark(watermark, notifyChanged: false, notifyWatermarkChanged: false);
            }
            QueueWatermarkDefaultsSave();
        }

        private void UpdateWatermarkTextHistoryUi()
        {
            if (WatermarkTextHistoryHost == null || WatermarkTextHistoryPanel == null) return;
            WatermarkTextHistoryHost.Visibility = Visibility.Visible;
            WatermarkTextHistoryPanel.Children.Clear();
            foreach (WatermarkPreset preset in _settings.WatermarkPresetHistory.Take(12))
            {
                var button = new Button
                {
                    Tag = preset.Clone(),
                    ToolTip = MakeWatermarkPresetPreview(preset),
                    Height = 28,
                    MaxWidth = 260,
                    Margin = new Thickness(0, 0, 6, 6),
                    Padding = new Thickness(8, 0, 8, 0),
                    Style = (Style)FindResource("ToolButton"),
                    Content = new TextBlock
                    {
                        Text = MakeWatermarkPresetPreview(preset),
                        FontSize = 11,
                        MaxWidth = 236,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                };
                button.Click += WatermarkTextHistory_Click;
                WatermarkTextHistoryPanel.Children.Add(button);
            }
        }

        private static string MakeWatermarkTextPreview(string text)
        {
            string preview = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            return preview.Length > 10 ? preview.Substring(0, 10) + "\u2026" : preview;
        }

        private static string MakeWatermarkPresetPreview(WatermarkPreset preset)
        {
            if (preset == null) return string.Empty;
            if (preset.Template == "ImageLogo")
                return $"Logo \u00b7 {preset.LogoScalePercent:0}%";
            string layout = preset.Template == "SingleText" ? "\u5355\u4e2a" : preset.Template == "TiledText" ? "\u5e73\u94fa" : "\u7f51\u683c";
            string weight = preset.Bold ? "\u52a0\u7c97" : "\u5e38\u89c4";
            return $"{MakeWatermarkTextPreview(preset.Text)} \u00b7 {layout} \u00b7 {weight} \u00b7 {preset.FontSize:0}px";
        }

        private void WatermarkTextHistory_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is WatermarkPreset preset)) return;
            ApplyWatermarkPreset(preset);
            WatermarkTextBox.Focus();
        }

        private void WatermarkTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button)) return;
            SelectWatermarkTemplate(button.Tag as string, true);
        }

        private void SelectWatermarkTemplate(string template, bool resetSinglePosition)
        {
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
                case "ImageLogo":
                    _watermarkStyle = WatermarkStyle.ImageLogo;
                    _watermarkLayout = WatermarkLayout.Single;
                    WatermarkAngleSlider.Value = 0;
                    WatermarkOffsetSlider.Value = 0;
                    if (string.IsNullOrWhiteSpace(_watermarkLogoPath))
                    {
                        WatermarkAssetManager.PruneMissing(_settings);
                        _watermarkLogoPath = _settings.WatermarkLogoAssets.FirstOrDefault() ?? string.Empty;
                    }
                    break;
                default:
                    _watermarkStyle = WatermarkStyle.DiamondGrid;
                    _watermarkLayout = WatermarkLayout.Tiled;
                    WatermarkAngleSlider.Value = 0;
                    break;
            }
            if (_watermarkStyle != WatermarkStyle.ImageLogo)
                _watermarkColorUserSelected = false;
            _initializingWatermarkUi = false;
            _settings.WatermarkTemplate = NormalizeWatermarkTemplate(template);
            QueueWatermarkDefaultsSave();
            UpdateWatermarkControls();
            ApplyWatermarkFromControls(resetSinglePosition);
            if (template == "ImageLogo" && string.IsNullOrWhiteSpace(_watermarkLogoPath))
                AddLogoWatermarkFromDialog();
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
            _watermarkColorUserSelected = true;
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
            QueueWatermarkDefaultsSave();
        }

        private void BtnAddLogoWatermark_Click(object sender, RoutedEventArgs e)
        {
            _hasSelectedWatermarkTemplate = true;
            _watermarkStyle = WatermarkStyle.ImageLogo;
            _watermarkLayout = WatermarkLayout.Single;
            WatermarkParametersPanel.Visibility = Visibility.Visible;
            UpdateWatermarkControls();
            AddLogoWatermarkFromDialog();
        }

        private void AddLogoWatermarkFromDialog()
        {
            var dialog = new OpenFileDialog
            {
                Filter = WatermarkAssetManager.Filter,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;

            if (_settings.WatermarkLogoAssets.Count >= Math.Max(1, _settings.WatermarkAssetLimit))
            {
                var confirm = AppDialog.Show(this,
                    $"常用 Logo 已达到 {_settings.WatermarkAssetLimit} 个。继续添加会移除最旧的 Logo。\n\n需要更多素材位，可以在右上角菜单的“历史缓存”里调整素材上限。",
                    "Logo 素材上限",
                    MessageBoxButton.OKCancel);
                if (confirm != MessageBoxResult.OK) return;
            }

            try
            {
                string removed;
                _watermarkLogoPath = WatermarkAssetManager.ImportAsset(dialog.FileName, _settings, out removed);
                _settings.Save();
                UpdateRecentLogoUi();
                ApplyWatermarkFromControls(true);
                UpdateStatus("已添加 Logo 水印。");
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, $"Logo 添加失败：{ex.Message}", "提示");
            }
        }

        private void RecentLogo_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is string path)) return;
            _watermarkLogoPath = path;
            WatermarkAssetManager.Touch(path, _settings);
            _settings.Save();
            UpdateRecentLogoUi();
            ApplyWatermarkFromControls(true);
        }

        private void WatermarkLogoFlip_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button)) return;
            if ((button.Tag as string) == "H")
                _watermarkLogoFlipHorizontal = !_watermarkLogoFlipHorizontal;
            else
                _watermarkLogoFlipVertical = !_watermarkLogoFlipVertical;
            UpdateWatermarkControls();
            ApplyWatermarkFromControls();
        }

        private void UpdateRecentLogoUi()
        {
            if (RecentLogoPanel == null) return;
            WatermarkAssetManager.PruneMissing(_settings);
            RecentLogoPanel.Children.Clear();
            foreach (string path in _settings.WatermarkLogoAssets.Take(3))
            {
                var button = new Button
                {
                    Tag = path,
                    Width = 54,
                    Height = 42,
                    Margin = new Thickness(0, 0, 6, 6),
                    Style = (Style)FindResource("ToolButton"),
                    ToolTip = Path.GetFileName(path)
                };

                BitmapSource thumbnail = LoadLogoThumbnail(path);
                if (thumbnail != null)
                {
                    button.Content = new Image
                    {
                        Source = thumbnail,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(3)
                    };
                }
                else
                {
                    button.Content = new TextBlock
                    {
                        Text = Path.GetExtension(path)?.TrimStart('.').ToUpperInvariant() ?? "LOGO",
                        FontSize = 11
                    };
                }
                button.Click += RecentLogo_Click;
                RecentLogoPanel.Children.Add(button);
            }
        }

        private static BitmapSource LoadLogoThumbnail(string path)
        {
            try
            {
                if (string.Equals(Path.GetExtension(path), ".webp", StringComparison.OrdinalIgnoreCase))
                    return WebpDecoder.Load(path);

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 96;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }

        private void ApplyWatermarkFromControls(bool resetSinglePosition = false)
        {
            if (_initializingWatermarkUi || !_hasSelectedWatermarkTemplate || Canvas1 == null || Canvas1.Image == null) return;
            var current = Canvas1.GetWatermark();
            ApplyAdaptiveWatermarkColorIfNeeded(Canvas1.Image);
            Canvas1.SetWatermark(new WatermarkSettings
            {
                Enabled = true,
                Text = WatermarkTextBox.Text,
                Style = _watermarkStyle,
                Layout = _watermarkLayout,
                Opacity = WatermarkOpacitySlider.Value / 100.0,
                FontSize = WatermarkFontSizeSlider.Value,
                FontFamilyName = _watermarkFontFamily,
                Bold = _watermarkBold,
                Angle = WatermarkAngleSlider.Value,
                Spacing = WatermarkSpacingSlider.Value,
                HorizontalOffset = WatermarkOffsetSlider.Value,
                VerticalOffset = resetSinglePosition ? 0 : current?.VerticalOffset ?? 0,
                Color = _watermarkColor,
                LogoPath = _watermarkLogoPath,
                LogoScalePercent = WatermarkLogoScaleSlider.Value,
                LogoFlipHorizontal = _watermarkLogoFlipHorizontal,
                LogoFlipVertical = _watermarkLogoFlipVertical
            }, notifyChanged: false, notifyWatermarkChanged: false);
            if (!_batchWatermarkMode)
                _hasUnsavedChanges = true;
            QueueWatermarkDefaultsSave();
        }

        private void ApplyAdaptiveWatermarkColorIfNeeded(BitmapSource image)
        {
            if (_watermarkColorUserSelected || _watermarkStyle == WatermarkStyle.ImageLogo || image == null) return;
            _watermarkColor = EstimateImageLuminance(image) < 0.48 ? Colors.White : Colors.Black;
        }

        private static double EstimateImageLuminance(BitmapSource source)
        {
            try
            {
                const int sampleSize = 64;
                double scale = Math.Min(1.0, sampleSize / Math.Max(1.0, Math.Max(source.PixelWidth, source.PixelHeight)));
                BitmapSource sampled = scale < 1.0
                    ? new TransformedBitmap(source, new ScaleTransform(scale, scale))
                    : source;
                BitmapSource bitmap = sampled.Format == PixelFormats.Bgra32 || sampled.Format == PixelFormats.Pbgra32
                    ? sampled
                    : new FormatConvertedBitmap(sampled, PixelFormats.Bgra32, null, 0);

                int width = Math.Max(1, bitmap.PixelWidth);
                int height = Math.Max(1, bitmap.PixelHeight);
                int stride = width * 4;
                var pixels = new byte[stride * height];
                bitmap.CopyPixels(pixels, stride, 0);

                double total = 0;
                double weight = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte b = pixels[i];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];
                    byte a = pixels[i + 3];
                    if (a < 24) continue;

                    double alpha = a / 255.0;
                    total += ((0.2126 * r) + (0.7152 * g) + (0.0722 * b)) / 255.0 * alpha;
                    weight += alpha;
                }

                return weight <= 0 ? 1.0 : total / weight;
            }
            catch
            {
                return 1.0;
            }
        }

        private static string GetWatermarkTemplate(WatermarkSettings watermark)
        {
            if (watermark == null) return "CertificateGrid";
            if (watermark.Style == WatermarkStyle.ImageLogo) return "ImageLogo";
            if (watermark.Layout == WatermarkLayout.Single) return "SingleText";
            return watermark.Style == WatermarkStyle.TextOnly ? "TiledText" : "CertificateGrid";
        }

        private void Canvas1_WatermarkChanged(object sender, EventArgs e)
        {
            if (_syncingWatermarkFromCanvas) return;
            var watermark = Canvas1.GetWatermark();
            if (watermark == null) return;
            _syncingWatermarkFromCanvas = true;
            _initializingWatermarkUi = true;
            _hasSelectedWatermarkTemplate = true;
            _watermarkStyle = watermark.Style;
            _watermarkLayout = watermark.Layout;
            _watermarkLogoPath = watermark.LogoPath ?? string.Empty;
            _watermarkLogoFlipHorizontal = watermark.LogoFlipHorizontal;
            _watermarkLogoFlipVertical = watermark.LogoFlipVertical;
            _watermarkColor = watermark.Color;
            _watermarkColorUserSelected = true;
            _watermarkFontFamily = string.IsNullOrWhiteSpace(watermark.FontFamilyName) ? "Microsoft YaHei UI" : watermark.FontFamilyName;
            _watermarkBold = watermark.Bold;
            WatermarkTextBox.Text = watermark.Text ?? string.Empty;
            WatermarkOpacitySlider.Value = Math.Max(WatermarkOpacitySlider.Minimum, Math.Min(WatermarkOpacitySlider.Maximum, watermark.Opacity * 100));
            WatermarkFontSizeSlider.Value = Math.Max(WatermarkFontSizeSlider.Minimum, Math.Min(WatermarkFontSizeSlider.Maximum, watermark.FontSize));
            WatermarkAngleSlider.Value = Math.Max(WatermarkAngleSlider.Minimum, Math.Min(WatermarkAngleSlider.Maximum, watermark.Angle));
            WatermarkSpacingSlider.Value = Math.Max(WatermarkSpacingSlider.Minimum, Math.Min(WatermarkSpacingSlider.Maximum, watermark.Spacing));
            WatermarkLogoScaleSlider.Value = Math.Max(WatermarkLogoScaleSlider.Minimum, Math.Min(WatermarkLogoScaleSlider.Maximum, watermark.LogoScalePercent <= 0 ? 18 : watermark.LogoScalePercent));
            WatermarkOffsetSlider.Value = Math.Max(WatermarkOffsetSlider.Minimum, Math.Min(WatermarkOffsetSlider.Maximum, watermark.HorizontalOffset));
            _settings.WatermarkTemplate = GetWatermarkTemplate(watermark);
            _initializingWatermarkUi = false;
            _syncingWatermarkFromCanvas = false;
            UpdateWatermarkControls();
            QueueWatermarkDefaultsSave();
        }

        private void UpdateWatermarkControls()
        {
            if (WatermarkOpacityValue == null) return;
            WatermarkOpacityValue.Text = $"{WatermarkOpacitySlider.Value:0}%";
            WatermarkFontSizeValue.Text = $"{WatermarkFontSizeSlider.Value:0}px";
            WatermarkAngleValue.Text = $"{WatermarkAngleSlider.Value:+0;-0;0}°";
            WatermarkSpacingValue.Text = $"{WatermarkSpacingSlider.Value:0}px";
            WatermarkOffsetValue.Text = $"{WatermarkOffsetSlider.Value:+0;-0;0}px";
            WatermarkLogoScaleValue.Text = $"{WatermarkLogoScaleSlider.Value:0}%";
            SetChoiceButtonState(WatermarkGridCard, _hasSelectedWatermarkTemplate && _watermarkStyle == WatermarkStyle.DiamondGrid);
            SetChoiceButtonState(WatermarkTiledCard, _hasSelectedWatermarkTemplate && _watermarkStyle == WatermarkStyle.TextOnly && _watermarkLayout == WatermarkLayout.Tiled);
            SetChoiceButtonState(WatermarkSingleCard, _hasSelectedWatermarkTemplate && _watermarkLayout == WatermarkLayout.Single && _watermarkStyle != WatermarkStyle.ImageLogo);
            SetChoiceButtonState(WatermarkLogoCard, _hasSelectedWatermarkTemplate && _watermarkStyle == WatermarkStyle.ImageLogo);
            SetChoiceButtonState(WatermarkBoldBtn, _watermarkBold);
            SetChoiceButtonState(WatermarkLogoFlipHBtn, _watermarkLogoFlipHorizontal);
            SetChoiceButtonState(WatermarkLogoFlipVBtn, _watermarkLogoFlipVertical);
            SetChoiceButtonState(WatermarkFontYaHeiBtn, _watermarkFontFamily == "Microsoft YaHei UI");
            SetChoiceButtonState(WatermarkFontSimSunBtn, _watermarkFontFamily == "SimSun");
            SetChoiceButtonState(WatermarkFontSimHeiBtn, _watermarkFontFamily == "SimHei");
            SetChoiceButtonState(WatermarkFontKaiTiBtn, _watermarkFontFamily == "KaiTi");
            bool single = _watermarkLayout == WatermarkLayout.Single;
            bool logo = _watermarkStyle == WatermarkStyle.ImageLogo;
            WatermarkSpacingHeader.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
            WatermarkSpacingSlider.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
            WatermarkOffsetHeader.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
            WatermarkOffsetSlider.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
            LogoParametersPanel.Visibility = logo ? Visibility.Visible : Visibility.Collapsed;
            WatermarkTextLabel.Visibility = logo ? Visibility.Collapsed : Visibility.Visible;
            WatermarkTextBox.Visibility = logo ? Visibility.Collapsed : Visibility.Visible;
            UpdateWatermarkTextHistoryUi();
            WatermarkFontLabel.Visibility = logo ? Visibility.Collapsed : Visibility.Visible;
            WatermarkFontPanel.Visibility = logo ? Visibility.Collapsed : Visibility.Visible;
            WatermarkFontSizeHeader.Visibility = logo ? Visibility.Collapsed : Visibility.Visible;
            WatermarkFontSizePanel.Visibility = logo ? Visibility.Collapsed : Visibility.Visible;
            WatermarkColorLabel.Visibility = logo ? Visibility.Collapsed : Visibility.Visible;
            WatermarkColorPanel.Visibility = logo ? Visibility.Collapsed : Visibility.Visible;
            LogoMorePanel.Visibility = logo ? Visibility.Visible : Visibility.Collapsed;
            SinglePositionPanel.Visibility = single ? Visibility.Visible : Visibility.Collapsed;
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
                case "LogoScale": WatermarkLogoScaleSlider.Value = 18; break;
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

        private void WatermarkAnchor_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || Canvas1.Image == null) return;
            var current = Canvas1.GetWatermark();
            if (current == null) return;

            double imageWidth = Canvas1.Image.PixelWidth;
            double imageHeight = Canvas1.Image.PixelHeight;
            double marginX = imageWidth * 0.08;
            double marginY = imageHeight * 0.08;
            double centerX = imageWidth / 2.0;
            double centerY = imageHeight / 2.0;

            switch (button.Tag as string)
            {
                case "TopLeft":
                    centerX = marginX;
                    centerY = marginY;
                    break;
                case "TopRight":
                    centerX = imageWidth - marginX;
                    centerY = marginY;
                    break;
                case "BottomLeft":
                    centerX = marginX;
                    centerY = imageHeight - marginY;
                    break;
                case "BottomRight":
                    centerX = imageWidth - marginX;
                    centerY = imageHeight - marginY;
                    break;
            }

            current.HorizontalOffset = centerX - imageWidth / 2.0;
            current.VerticalOffset = centerY - imageHeight / 2.0;
            Canvas1.SetWatermark(current);
            UpdateWatermarkControls();
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
            UpdateStatus("已移除水印。");
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
            if (tag != "Crop" && Canvas1.CurrentTool == AnnotationTool.Crop) Canvas1.CancelCrop();
            Canvas1.CurrentTool = (AnnotationTool)Enum.Parse(typeof(AnnotationTool), tag);
            if (tag == "Crop") Canvas1.BeginCrop();
            CropPanel.Visibility = tag == "Crop" ? Visibility.Visible : Visibility.Collapsed;
            SetToolButtonState(new[] { BtnSelect, BtnRect, BtnEllipse, BtnArrow, BtnFreehand, BtnMosaic, BtnText }, tag, Brushes.Transparent);
            SetToolButtonState(new[] { PanelBtnRect, PanelBtnEllipse, PanelBtnArrow, PanelBtnFreehand, PanelBtnMosaic, PanelBtnText, PanelBtnCrop }, tag, null, true);
            ResetBottomPanButton();
            _settings.Tool = tag;
            UpdateAnnotationPropertiesPanel();
        }

        private void CropAspect_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is string tag)) return;
            double? ratio = null;
            switch (tag)
            {
                case "1:1": ratio = 1.0; break;
                case "4:3": ratio = 4.0 / 3.0; break;
                case "3:4": ratio = 3.0 / 4.0; break;
                case "16:9": ratio = 16.0 / 9.0; break;
                case "9:16": ratio = 9.0 / 16.0; break;
            }
            Canvas1.SetCropAspectRatio(ratio);
        }

        private void ConfirmCropAction()
        {
            if (!Canvas1.HasPendingCrop) return;
            CaptureHistoryVersion("裁剪");
            Canvas1.ConfirmCrop();
            SetActiveTool("Select");
            UpdateStatus("已裁剪图片。");
        }

        private void BtnCropConfirm_Click(object sender, RoutedEventArgs e) => ConfirmCropAction();

        private void BtnCropCancel_Click(object sender, RoutedEventArgs e)
        {
            Canvas1.CancelCrop();
            SetActiveTool("Select");
        }

        private void BtnBatchCrop_Click(object sender, RoutedEventArgs e)
        {
            var window = new BatchCropWindow(GetCurrentDiskPath()) { Owner = this };
            window.ShowDialog();
        }

        private void BtnCollage_Click(object sender, RoutedEventArgs e)
        {
            var window = new CollageWindow(GetCurrentDiskPath()) { Owner = this };
            window.ShowDialog();
        }

        public void OpenBatchCropForPath(string targetPath)
        {
            var window = new BatchCropWindow(targetPath) { Owner = this };
            window.ShowDialog();
        }

        private void BtnBatchWatermark_Click(object sender, RoutedEventArgs e)
        {
            ApplyWatermarkFromControls();
            EnterBatchWatermarkMode();
        }

        private void EnterBatchWatermarkMode()
        {
            string initialPath = _currentFilePath;
            _batchWatermarkMode = true;
            _batchWatermarkFiles.Clear();
            _batchWatermarkIndex = -1;
            if (!string.IsNullOrWhiteSpace(initialPath) && File.Exists(initialPath) && IsSupportedImagePath(initialPath))
            {
                _batchWatermarkFiles.Add(initialPath);
                _batchWatermarkIndex = 0;
            }

            SetEditMode(true, false);
            WatermarkPanel.Visibility = Visibility.Visible;
            WatermarkParametersPanel.Visibility = Visibility.Visible;
            SetActiveTool("Select");
            UpdateAnnotationPropertiesPanel();
            BatchWatermarkBar.Visibility = Visibility.Visible;
            BatchBar.Visibility = Visibility.Collapsed;
            _hasUnsavedChanges = false;
            UpdateBatchWatermarkBar();
            UpdateStatus("已进入批量水印模式：继续用右侧水印面板和画布拖动来调整。");
        }

        private void ExitBatchWatermarkMode()
        {
            _batchWatermarkMode = false;
            _batchWatermarkProcessing = false;
            _batchWatermarkFiles.Clear();
            _batchWatermarkIndex = -1;
            BatchWatermarkBar.Visibility = Visibility.Collapsed;
            if (BtnBatchWatermark != null)
                BtnBatchWatermark.Visibility = Visibility.Visible;
            BatchWatermarkProgress.Value = 0;
            _hasUnsavedChanges = false;
            UpdateStatus("已退出批量水印模式。");
        }

        private void BtnBatchWatermarkExit_Click(object sender, RoutedEventArgs e) => ExitBatchWatermarkMode();

        private void BtnBatchWatermarkAddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.webp|所有文件|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog(this) == true)
                AddBatchWatermarkFiles(dlg.FileNames, true);
        }

        private void BtnBatchWatermarkAddFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                if (!string.IsNullOrWhiteSpace(_currentFilePath))
                {
                    string dir = Path.GetDirectoryName(_currentFilePath);
                    if (Directory.Exists(dir)) dlg.SelectedPath = dir;
                }
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                var files = Directory.EnumerateFiles(dlg.SelectedPath)
                    .Where(path => Array.IndexOf(SupportedExtensions, Path.GetExtension(path).ToLowerInvariant()) >= 0)
                    .ToArray();
                AddBatchWatermarkFiles(files, true, true);
            }
        }

        private void AddBatchWatermarkFiles(IEnumerable<string> paths, bool previewFirstNew, bool replaceExisting = false)
        {
            if (!_batchWatermarkMode)
                EnterBatchWatermarkMode();

            if (replaceExisting)
            {
                _batchWatermarkFiles.Clear();
                _batchWatermarkIndex = -1;
            }

            string firstNew = null;
            foreach (string path in paths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (Array.IndexOf(SupportedExtensions, ext) < 0) continue;
                if (_batchWatermarkFiles.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
                _batchWatermarkFiles.Add(path);
                if (firstNew == null) firstNew = path;
            }

            if (_batchWatermarkIndex < 0 && _batchWatermarkFiles.Count > 0)
                _batchWatermarkIndex = 0;

            if (previewFirstNew && firstNew != null)
            {
                int index = _batchWatermarkFiles.FindIndex(p => string.Equals(p, firstNew, StringComparison.OrdinalIgnoreCase));
                LoadBatchWatermarkPreview(index);
            }
            else
            {
                UpdateBatchWatermarkBar();
            }
        }

        private void BtnBatchWatermarkPrev_Click(object sender, RoutedEventArgs e) =>
            LoadBatchWatermarkPreview(_batchWatermarkIndex - 1);

        private void BtnBatchWatermarkNext_Click(object sender, RoutedEventArgs e) =>
            LoadBatchWatermarkPreview(_batchWatermarkIndex + 1);

        private void LoadBatchWatermarkPreview(int index)
        {
            if (index < 0 || index >= _batchWatermarkFiles.Count) return;
            var currentWatermark = Canvas1.GetWatermark();
            var profile = BatchWatermarkProfile.Create(currentWatermark, Canvas1.Image);
            _batchWatermarkIndex = index;

            LoadImage(_batchWatermarkFiles[index], false);
            WatermarkPanel.Visibility = Visibility.Visible;
            WatermarkParametersPanel.Visibility = Visibility.Visible;
            SetEditMode(true, false);
            SetActiveTool("Select");
            var adapted = profile.CreateFor(Canvas1.Image);
            Canvas1.SetWatermark(adapted, notifyChanged: false, notifyWatermarkChanged: true);
            _hasSelectedWatermarkTemplate = adapted != null;
            _hasUnsavedChanges = false;
            UpdateBatchWatermarkBar();
        }

        private async void BtnBatchWatermarkRun_Click(object sender, RoutedEventArgs e)
        {
            if (_batchWatermarkProcessing) return;
            if (_batchWatermarkFiles.Count == 0)
            {
                AppDialog.Show(this, "请先添加要批量处理的图片。", "提示");
                return;
            }

            var watermark = Canvas1.GetWatermark();
            if (watermark == null || !watermark.Enabled)
            {
                AppDialog.Show(this, "请先添加一个水印。", "提示");
                return;
            }

            var profile = BatchWatermarkProfile.Create(watermark, Canvas1.Image);
            var files = _batchWatermarkFiles.ToList();
            _batchWatermarkProcessing = true;
            BtnBatchWatermarkRun.IsHitTestVisible = false;
            BtnBatchWatermarkRun.Content = "处理中...";
            BatchWatermarkProgress.Maximum = files.Count;
            BatchWatermarkProgress.Value = 0;
            UpdateBatchWatermarkBar();

            int succeeded = 0;
            var failed = new List<string>();
            var outputDirectories = new List<string>();
            try
            {
                foreach (string path in files)
                {
                    UpdateStatus($"正在批量加水印：{Path.GetFileName(path)}");
                    var result = await System.Threading.Tasks.Task.Run(() =>
                        ProcessBatchWatermarkFile(path, profile, GetBatchWatermarkOutputDirectory(path)));
                    if (result.Success)
                    {
                        succeeded++;
                        string outputDirectory = string.IsNullOrWhiteSpace(result.TargetPath) ? null : Path.GetDirectoryName(result.TargetPath);
                        if (!string.IsNullOrWhiteSpace(outputDirectory) &&
                            !outputDirectories.Any(dir => string.Equals(dir, outputDirectory, StringComparison.OrdinalIgnoreCase)))
                        {
                            outputDirectories.Add(outputDirectory);
                        }
                    }
                    else failed.Add($"{Path.GetFileName(path)}：{result.Message}");
                    BatchWatermarkProgress.Value += 1;
                }
            }
            finally
            {
                _batchWatermarkProcessing = false;
                BtnBatchWatermarkRun.IsHitTestVisible = true;
                BtnBatchWatermarkRun.Content = "开始输出";
                UpdateBatchWatermarkBar();
            }

            if (succeeded > 0)
                SaveCurrentWatermarkPresetToHistory();

            string summary = failed.Count == 0
                ? $"批量加水印完成：成功 {succeeded} 张。\n\n{GetBatchWatermarkOutputSummary(outputDirectories)}"
                : $"批量加水印完成：成功 {succeeded} 张，失败 {failed.Count} 张。\n\n失败文件：\n" +
                  string.Join("\n", failed.Take(20));
            ShowBatchWatermarkResultDialog(summary, outputDirectories.FirstOrDefault());
            UpdateStatus("批量水印输出完成。");
        }

        private static string GetBatchWatermarkOutputSummary(IReadOnlyList<string> outputDirectories)
        {
            if (outputDirectories == null || outputDirectories.Count == 0)
                return $"输出位置：每个来源文件夹下的 {BatchWatermarkOutputFolderName}";
            if (outputDirectories.Count == 1)
                return $"输出位置：{outputDirectories[0]}";
            return $"输出位置：已按来源文件夹分别保存到 {outputDirectories.Count} 个成品文件夹。";
        }

        private void ShowBatchWatermarkResultDialog(string message, string outputDirectory)
        {
            bool canOpenOutput = !string.IsNullOrWhiteSpace(outputDirectory) && Directory.Exists(outputDirectory);
            var dialog = new Window
            {
                Title = "批量加水印结果",
                Owner = this,
                Width = 500,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                FontFamily = UiFonts.Family
            };

            var root = new Grid { Margin = new Thickness(24) };
            var panel = new Border
            {
                Background = DialogPanelBrush(),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(22, 18, 22, 22),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 24,
                    ShadowDepth = 5,
                    Opacity = _editMode ? 0.5 : 0.18
                }
            };

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var titleBar = new Grid { Margin = new Thickness(0, 0, 0, 20), Cursor = Cursors.SizeAll };
            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 1)
                {
                    try { dialog.DragMove(); }
                    catch (InvalidOperationException) { }
                }
            };
            titleBar.Children.Add(new TextBlock
            {
                Text = "批量加水印结果",
                Foreground = DialogPrimaryTextBrush(),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetRow(titleBar, 0);
            layout.Children.Add(titleBar);

            var messageText = new TextBlock
            {
                Text = message,
                Foreground = DialogSecondaryTextBrush(),
                FontSize = 14,
                LineHeight = 22,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 28)
            };
            Grid.SetRow(messageText, 1);
            layout.Children.Add(messageText);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            if (canOpenOutput)
            {
                var openButton = MakeBatchResultButton("打开成品文件夹", true);
                openButton.Click += (s, e) =>
                {
                    OpenFolder(outputDirectory);
                    dialog.Close();
                };
                actions.Children.Add(openButton);
            }
            var doneButton = MakeBatchResultButton("完成", !canOpenOutput);
            doneButton.Click += (s, e) => dialog.Close();
            actions.Children.Add(doneButton);
            Grid.SetRow(actions, 2);
            layout.Children.Add(actions);

            panel.Child = layout;
            root.Children.Add(panel);
            dialog.Content = root;
            dialog.ShowDialog();
        }

        private Button MakeBatchResultButton(string text, bool primary)
        {
            return new Button
            {
                Content = text,
                MinWidth = primary ? 118 : 72,
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(14, 0, 14, 0),
                FontWeight = primary ? FontWeights.Bold : FontWeights.Normal,
                Cursor = Cursors.Hand,
                Background = primary ? (Brush)FindResource("AccentBrush") : DialogSecondaryButtonBrush(),
                BorderBrush = primary ? (Brush)FindResource("AccentBrush") : DialogBorderBrush(),
                Foreground = primary ? Brushes.White : DialogPrimaryTextBrush(),
                BorderThickness = new Thickness(1)
            };
        }

        private void OpenFolder(string folder)
        {
            try
            {
                if (Directory.Exists(folder))
                    Process.Start("explorer.exe", $"\"{folder}\"");
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, $"无法打开成品文件夹：{ex.Message}", "提示");
            }
        }

        private void UpdateBatchWatermarkBar()
        {
            if (!_batchWatermarkMode)
            {
                BatchWatermarkBar.Visibility = Visibility.Collapsed;
                if (BtnBatchWatermark != null)
                    BtnBatchWatermark.Visibility = Visibility.Visible;
                return;
            }

            BatchWatermarkBar.Visibility = Visibility.Visible;
            if (BtnBatchWatermark != null)
                BtnBatchWatermark.Visibility = Visibility.Collapsed;
            BatchBar.Visibility = Visibility.Collapsed;
            string current = _batchWatermarkIndex >= 0 && _batchWatermarkIndex < _batchWatermarkFiles.Count
                ? Path.GetFileName(_batchWatermarkFiles[_batchWatermarkIndex])
                : "尚未添加图片";
            BatchWatermarkInfoText.Text =
                _batchWatermarkFiles.Count == 0
                    ? $"请先添加文件夹；输出到每个来源文件夹的 {BatchWatermarkOutputFolderName}"
                    : $"共 {_batchWatermarkFiles.Count} 张，当前：{current}；输出到每个来源文件夹的 {BatchWatermarkOutputFolderName}";
            bool hasFiles = _batchWatermarkFiles.Count > 0;
            SetBatchWatermarkButtonStyle(BtnBatchWatermarkAddFolder, !hasFiles && !_batchWatermarkProcessing);
            SetBatchWatermarkButtonStyle(BtnBatchWatermarkRun, hasFiles || _batchWatermarkProcessing);
            SetBatchWatermarkButtonStyle(BtnBatchWatermarkAddFiles, false);
            SetBatchWatermarkButtonStyle(BtnBatchWatermarkPrev, false);
            SetBatchWatermarkButtonStyle(BtnBatchWatermarkNext, false);
            SetBatchWatermarkButtonStyle(BtnBatchWatermarkExit, false);
            BtnBatchWatermarkAddFiles.IsEnabled = !_batchWatermarkProcessing;
            BtnBatchWatermarkAddFolder.IsEnabled = !_batchWatermarkProcessing;
            BtnBatchWatermarkPrev.IsEnabled = _batchWatermarkIndex > 0 && !_batchWatermarkProcessing;
            BtnBatchWatermarkNext.IsEnabled = _batchWatermarkIndex >= 0 && _batchWatermarkIndex < _batchWatermarkFiles.Count - 1 && !_batchWatermarkProcessing;
            BtnBatchWatermarkRun.IsEnabled = hasFiles && !_batchWatermarkProcessing;
            BtnBatchWatermarkExit.IsEnabled = !_batchWatermarkProcessing;
        }

        private void SetBatchWatermarkButtonStyle(Button button, bool primary)
        {
            if (button == null) return;
            button.Style = (Style)FindResource(primary ? "WatermarkPrimaryButton" : "WatermarkSecondaryButton");
        }

        private string GetBatchWatermarkOutputDirectory(string sourcePath)
        {
            string folder = Path.GetDirectoryName(sourcePath);
            if (string.IsNullOrWhiteSpace(folder))
                folder = !string.IsNullOrWhiteSpace(_currentFilePath) ? Path.GetDirectoryName(_currentFilePath) : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            return Path.Combine(folder, BatchWatermarkOutputFolderName);
        }

        private static BatchWatermarkResult ProcessBatchWatermarkFile(string sourcePath, BatchWatermarkProfile profile, string outputDirectory)
        {
            var result = new BatchWatermarkResult { SourcePath = sourcePath };
            try
            {
                BitmapSource source = ImageConversionService.LoadBitmap(sourcePath);
                WatermarkSettings watermark = profile.CreateFor(source);
                BitmapSource rendered = RenderWatermarkForBatch(source, watermark);

                Directory.CreateDirectory(outputDirectory);
                string extension = Path.GetExtension(sourcePath);
                if (string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
                    extension = ".jpg";
                if (!ImageConversionService.IsSupportedOutput(extension))
                    extension = ".png";

                string targetPath = BuildBatchWatermarkTargetPath(sourcePath, outputDirectory, extension);
                byte[] bytes = ImageConversionService.EncodeBitmap(rendered, extension, 95);
                File.WriteAllBytes(targetPath, bytes);
                result.Success = true;
                result.TargetPath = targetPath;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
            }
            return result;
        }

        private static BitmapSource RenderWatermarkForBatch(BitmapSource source, WatermarkSettings watermark)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
                watermark?.Draw(dc, source);
            }

            var bitmap = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private static string BuildBatchWatermarkTargetPath(string sourcePath, string outputDirectory, string extension)
        {
            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            string path = Path.Combine(outputDirectory, baseName + extension);
            int index = 2;
            while (File.Exists(path))
            {
                path = Path.Combine(outputDirectory, $"{baseName}({index}){extension}");
                index++;
            }
            return path;
        }

        private sealed class BatchWatermarkResult
        {
            public string SourcePath { get; set; }
            public string TargetPath { get; set; }
            public bool Success { get; set; }
            public string Message { get; set; }
        }

        private sealed class BatchWatermarkProfile
        {
            private readonly WatermarkSettings _settings;
            private readonly bool _single;
            private readonly AnchorX _anchorX;
            private readonly AnchorY _anchorY;
            private readonly double _xRatio;
            private readonly double _yRatio;

            private BatchWatermarkProfile(
                WatermarkSettings settings,
                bool single,
                AnchorX anchorX,
                AnchorY anchorY,
                double xRatio,
                double yRatio)
            {
                _settings = settings?.Clone();
                _single = single;
                _anchorX = anchorX;
                _anchorY = anchorY;
                _xRatio = xRatio;
                _yRatio = yRatio;
            }

            public static BatchWatermarkProfile Create(WatermarkSettings settings, BitmapSource referenceImage)
            {
                WatermarkSettings clone = settings?.Clone();
                bool single = clone != null && (clone.Style == WatermarkStyle.ImageLogo || clone.Layout == WatermarkLayout.Single);
                if (clone == null || !single || referenceImage == null)
                    return new BatchWatermarkProfile(clone, false, AnchorX.Center, AnchorY.Center, 0, 0);

                Rect bounds = EstimateSingleBounds(clone, referenceImage);
                double width = Math.Max(1, referenceImage.PixelWidth);
                double height = Math.Max(1, referenceImage.PixelHeight);
                double centerX = bounds.Left + bounds.Width / 2.0;
                double centerY = bounds.Top + bounds.Height / 2.0;

                AnchorX anchorX = centerX < width / 3.0 ? AnchorX.Left :
                    centerX > width * 2.0 / 3.0 ? AnchorX.Right : AnchorX.Center;
                AnchorY anchorY = centerY < height / 3.0 ? AnchorY.Top :
                    centerY > height * 2.0 / 3.0 ? AnchorY.Bottom : AnchorY.Center;

                double xRatio = anchorX == AnchorX.Left ? bounds.Left / width :
                    anchorX == AnchorX.Right ? (width - bounds.Right) / width :
                    (centerX - width / 2.0) / width;
                double yRatio = anchorY == AnchorY.Top ? bounds.Top / height :
                    anchorY == AnchorY.Bottom ? (height - bounds.Bottom) / height :
                    (centerY - height / 2.0) / height;

                return new BatchWatermarkProfile(clone, true, anchorX, anchorY, ClampRatio(xRatio), ClampRatio(yRatio));
            }

            public WatermarkSettings CreateFor(BitmapSource image)
            {
                WatermarkSettings clone = _settings?.Clone();
                if (clone == null || !_single || image == null) return clone;

                double width = Math.Max(1, image.PixelWidth);
                double height = Math.Max(1, image.PixelHeight);
                Rect targetBounds = EstimateSingleBounds(clone, image);
                double centerX = _anchorX == AnchorX.Left ? _xRatio * width + targetBounds.Width / 2.0 :
                    _anchorX == AnchorX.Right ? width - _xRatio * width - targetBounds.Width / 2.0 :
                    width / 2.0 + _xRatio * width;
                double centerY = _anchorY == AnchorY.Top ? _yRatio * height + targetBounds.Height / 2.0 :
                    _anchorY == AnchorY.Bottom ? height - _yRatio * height - targetBounds.Height / 2.0 :
                    height / 2.0 + _yRatio * height;

                clone.Layout = WatermarkLayout.Single;
                clone.HorizontalOffset = centerX - width / 2.0;
                clone.VerticalOffset = centerY - height / 2.0;
                return clone;
            }

            private static Rect EstimateSingleBounds(WatermarkSettings settings, BitmapSource image)
            {
                double width = Math.Max(1, image.PixelWidth);
                double height = Math.Max(1, image.PixelHeight);
                double centerX = width / 2.0 + settings.HorizontalOffset;
                double centerY = height / 2.0 + settings.VerticalOffset;

                if (settings.Style == WatermarkStyle.ImageLogo)
                {
                    double shortEdge = Math.Max(1, Math.Min(width, height));
                    double logoWidth = Math.Max(12, shortEdge * Math.Max(1, Math.Min(80, settings.LogoScalePercent)) / 100.0);
                    double logoHeight = logoWidth * 0.45;
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(settings.LogoPath) && File.Exists(settings.LogoPath))
                        {
                            BitmapSource logo = ImageConversionService.LoadBitmap(settings.LogoPath);
                            logoHeight = logoWidth * logo.PixelHeight / Math.Max(1.0, logo.PixelWidth);
                        }
                    }
                    catch
                    {
                    }
                    return new Rect(centerX - logoWidth / 2.0, centerY - logoHeight / 2.0, logoWidth, logoHeight);
                }

                string text = settings.Text ?? string.Empty;
                string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                int longest = Math.Max(1, lines.Max(line => line.Length));
                double adaptiveScale = Math.Max(0.6, Math.Min(6.0, Math.Min(width, height) / 1080.0));
                double fontSize = Math.Max(8, settings.FontSize * adaptiveScale);
                double textWidth = Math.Min(width * 0.82, Math.Max(120, longest * fontSize * 0.58));
                double textHeight = Math.Max(34, Math.Max(1, lines.Length) * fontSize * 1.28);
                return new Rect(centerX - textWidth / 2.0, centerY - textHeight / 2.0, textWidth, textHeight);
            }

            private static double ClampRatio(double value) => Math.Max(-0.5, Math.Min(0.5, value));
        }

        private enum AnchorX { Left, Center, Right }
        private enum AnchorY { Top, Center, Bottom }

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
            if (_syncingAnnotationProperties) return;
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

        private void RefreshToolButtonStates()
        {
            if (Canvas1 == null) return;
            string tag = _panMode ? "Pan" : Canvas1.CurrentTool.ToString();
            SetToolButtonState(new[] { BtnSelect, BtnRect, BtnEllipse, BtnArrow, BtnFreehand, BtnMosaic, BtnText }, tag, Brushes.Transparent);
            SetToolButtonState(new[] { PanelBtnRect, PanelBtnEllipse, PanelBtnArrow, PanelBtnFreehand, PanelBtnMosaic, PanelBtnText, PanelBtnCrop }, tag, null, true);
        }

        private void SetToolButtonState(IEnumerable<Button> buttons, string tag, Brush normalBackground, bool usePanelSurface = false)
        {
            foreach (var btn in buttons)
            {
                bool active = (string)btn.Tag == tag;
                if (active)
                {
                    btn.Background = (Brush)FindResource("ActiveBrush");
                    btn.Foreground = Brushes.White;
                    btn.BorderBrush = (Brush)FindResource("ActiveBorderBrush");
                    btn.BorderThickness = new Thickness(2);
                    continue;
                }

                // 不把“查看模式”的画笔直接塞进按钮属性。使用动态资源后，
                // 切换到深色编辑器时图标、文字和卡片会一起更新。
                if (usePanelSurface)
                {
                    btn.SetResourceReference(Control.BackgroundProperty, "PanelToolSurfaceBrush");
                    btn.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
                    btn.SetResourceReference(Control.BorderBrushProperty, "PanelToolBorderBrush");
                    btn.BorderThickness = new Thickness(1);
                }
                else
                {
                    btn.Background = normalBackground ?? Brushes.Transparent;
                    btn.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
                    btn.SetResourceReference(Control.BorderBrushProperty, "BorderBrush1");
                    btn.BorderThickness = new Thickness(1);
                }
            }
        }

        private void UpdateAnnotationPropertiesPanel()
        {
            if (AnnotationPropertiesHint == null || MosaicPropertiesPanel == null) return;

            AnnotationPropertiesHint.Visibility = Visibility.Collapsed;
            MosaicPropertiesPanel.Visibility = Visibility.Collapsed;
            ColorPropertiesLabel.Visibility = Visibility.Collapsed;
            ColorPropertiesPanel.Visibility = Visibility.Collapsed;
            ThicknessPropertiesLabel.Visibility = Visibility.Collapsed;
            ThicknessPropertiesPanel.Visibility = Visibility.Collapsed;
            TextPropertiesLabel.Visibility = Visibility.Collapsed;
            TextPropertiesPanel.Visibility = Visibility.Collapsed;

            bool isSpecialPanelOpen =
                (WatermarkPanel != null && WatermarkPanel.Visibility == Visibility.Visible) ||
                (CropPanel != null && CropPanel.Visibility == Visibility.Visible);
            if (!_editMode || isSpecialPanelOpen) return;

            Annotation selected = Canvas1.Selected;
            bool hasSelection = selected != null;
            AnnotationTool tool = Canvas1.CurrentTool;
            bool showMosaic = selected is MosaicAnnotation || (!hasSelection && tool == AnnotationTool.Mosaic);
            bool showText = selected is TextAnnotation || (!hasSelection && tool == AnnotationTool.Text);
            bool showColor =
                (hasSelection && !(selected is MosaicAnnotation)) ||
                (!hasSelection && (tool == AnnotationTool.Rectangle || tool == AnnotationTool.Ellipse ||
                                   tool == AnnotationTool.Arrow || tool == AnnotationTool.Freehand ||
                                   tool == AnnotationTool.Text));
            bool showThickness =
                (hasSelection && !(selected is MosaicAnnotation) && !(selected is TextAnnotation)) ||
                (!hasSelection && (tool == AnnotationTool.Rectangle || tool == AnnotationTool.Ellipse ||
                                   tool == AnnotationTool.Arrow || tool == AnnotationTool.Freehand));

            if (showMosaic)
            {
                MosaicPropertiesPanel.Visibility = Visibility.Visible;
                var mosaic = selected as MosaicAnnotation;
                MosaicPanelTitle.Text = mosaic == null ? "新建马赛克" : "编辑马赛克";
                MosaicPanelHint.Text = mosaic == null
                    ? "从空白处拖动即可新增；点已有区域可继续调整。"
                    : "已选中：拖动内部可移动，抓手可缩放或旋转。";
                SyncMosaicControls(
                    mosaic?.Mode ?? Canvas1.CurrentMosaicMode,
                    mosaic?.BlockSize ?? Canvas1.CurrentMosaicStrength);
            }

            if (showColor)
            {
                ColorPropertiesLabel.Visibility = Visibility.Visible;
                ColorPropertiesPanel.Visibility = Visibility.Visible;
            }
            if (showThickness)
            {
                ThicknessPropertiesLabel.Visibility = Visibility.Visible;
                ThicknessPropertiesPanel.Visibility = Visibility.Visible;
            }
            if (showText)
            {
                TextPropertiesLabel.Visibility = Visibility.Visible;
                TextPropertiesPanel.Visibility = Visibility.Visible;
            }

            if (selected is TextAnnotation text)
            {
                Canvas1.CurrentFontSize = text.FontSize;
                SetActiveFontButton(((int)text.FontSize).ToString());
            }
            else if (selected != null && !(selected is MosaicAnnotation))
            {
                Canvas1.CurrentColor = selected.StrokeColor;
                if (showThickness)
                {
                    _syncingAnnotationProperties = true;
                    try
                    {
                        Canvas1.CurrentThickness = selected.Thickness;
                        if (ThicknessSlider != null && Math.Abs(ThicknessSlider.Value - selected.Thickness) > 0.001)
                            ThicknessSlider.Value = selected.Thickness;
                        if (ThicknessValueText != null)
                            ThicknessValueText.Text = ((int)Math.Round(selected.Thickness)).ToString();
                    }
                    finally
                    {
                        _syncingAnnotationProperties = false;
                    }
                }
            }

            if (!showMosaic && !showColor && !showThickness && !showText)
            {
                AnnotationPropertiesHint.Text = "点一下已画过的标注，即可继续移动或修改它。";
                AnnotationPropertiesHint.Visibility = Visibility.Visible;
            }
        }

        private void SyncMosaicControls(MosaicMode mode, int strength)
        {
            _syncingAnnotationProperties = true;
            try
            {
                Canvas1.CurrentMosaicMode = mode;
                Canvas1.CurrentMosaicStrength = strength;
                if (MosaicModePixelBtn != null)
                    SetChoiceButtonState(MosaicModePixelBtn, mode == MosaicMode.Pixelate);
                if (MosaicModeBlurBtn != null)
                    SetChoiceButtonState(MosaicModeBlurBtn, mode == MosaicMode.Blur);
                if (MosaicStrengthSlider != null && Math.Abs(MosaicStrengthSlider.Value - strength) > 0.001)
                    MosaicStrengthSlider.Value = strength;
                if (MosaicStrengthText != null)
                    MosaicStrengthText.Text = strength.ToString();
            }
            finally
            {
                _syncingAnnotationProperties = false;
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

        private void BtnNewMosaic_Click(object sender, RoutedEventArgs e)
        {
            SetEditMode(true, false);
            SetActiveTool("Mosaic");
            Canvas1.BeginNewAnnotation();
            UpdateAnnotationPropertiesPanel();
            UpdateStatus("新建马赛克：现在可在任意位置拖动，包括已有马赛克上方。");
        }

        private void SetMosaicMode(MosaicMode mode)
        {
            Canvas1.CurrentMosaicMode = mode;
            if (Canvas1.Selected is MosaicAnnotation)
                Canvas1.SetSelectedMosaicMode(mode);
            int strength = Canvas1.Selected is MosaicAnnotation selectedMosaic
                ? selectedMosaic.BlockSize
                : Canvas1.CurrentMosaicStrength;
            SyncMosaicControls(mode, strength);
            UpdateStatus(mode == MosaicMode.Blur ? "马赛克工具：高斯模糊模式。" : "马赛克工具：像素马赛克模式。");
        }

        private void MosaicStrengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_syncingAnnotationProperties) return;
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
            UpdateStatus($"马赛克/高斯模糊程度：{Canvas1.CurrentMosaicStrength}");
        }

        private void SetMosaicStrength(int strength)
        {
            strength = Math.Max(2, Math.Min(30, strength));
            if (Canvas1 != null)
            {
                Canvas1.CurrentMosaicStrength = strength;
                if (Canvas1.Selected is MosaicAnnotation)
                    Canvas1.SetSelectedMosaicStrength(strength);
            }
            int actualStrength = Canvas1?.Selected is MosaicAnnotation selectedMosaic
                ? selectedMosaic.BlockSize
                : strength;
            SyncMosaicControls(Canvas1.CurrentMosaicMode, actualStrength);
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e) => Canvas1.Undo();
        private void BtnRedo_Click(object sender, RoutedEventArgs e) => Canvas1.Redo();
        private void BtnDelete_Click(object sender, RoutedEventArgs e) => Canvas1.DeleteSelected();
        private void BtnZoomIn_Click(object sender, RoutedEventArgs e) { _fitToWindow = false; Canvas1.Scale *= 1.25; }
        private void BtnZoomOut_Click(object sender, RoutedEventArgs e) { _fitToWindow = false; Canvas1.Scale /= 1.25; }
        private void BtnZoomReset_Click(object sender, RoutedEventArgs e) { _fitToWindow = false; Canvas1.Scale = 1.0; }

        private void Canvas1_SelectionChanged(object sender, EventArgs e)
        {
            if (Canvas1.Selected is MosaicAnnotation mosaic)
            {
                SyncMosaicControls(mosaic.Mode, mosaic.BlockSize);
                UpdateStatus("已选中马赛克。可拖动、缩放、旋转，或在右侧切换效果。");
            }
            else if (Canvas1.Selected is TextAnnotation text)
            {
                Canvas1.CurrentFontSize = text.FontSize;
                SetActiveFontButton(((int)text.FontSize).ToString());
                UpdateStatus("已选中文字。可直接拖动，双击编辑内容和字号。");
            }
            else if (Canvas1.HasSelection)
            {
                UpdateStatus("已选中标注。可拖动、改色、改粗细或按 Delete 删除。");
            }
            UpdateAnnotationPropertiesPanel();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e) => SaveAnnotatedImage(false);

        private void BtnSaveMenu_Click(object sender, RoutedEventArgs e)
        {
            ApplyPopupTheme(_editMode);
            SavePopup.IsOpen = true;
        }

        private void MenuSaveProject_Click(object sender, RoutedEventArgs e)
        {
            SavePopup.IsOpen = false;
            SaveProject(false);
        }

        private void MenuSaveAs_Click(object sender, RoutedEventArgs e)
        {
            SavePopup.IsOpen = false;
            if (SaveAnnotatedImage(false))
                RecordRecentContextAction("SaveAs");
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
            SaveCurrentWatermarkPresetToHistory();
            RecordRecentContextAction("CopyImage");
            UpdateStatus("已复制标注后的图片到剪贴板。");
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
            RecordRecentContextAction("Print");
            UpdateStatus("已发送到打印机。");
        }

        private void BtnRotateMenu_Click(object sender, RoutedEventArgs e)
        {
            if (Canvas1.Image == null)
            {
                AppDialog.Show(this, "请先打开一张图片。", "提示");
                return;
            }

            ApplyPopupTheme(_editMode);
            RotatePopup.IsOpen = true;
        }

        private void ImageTransform_Click(object sender, RoutedEventArgs e)
        {
            if (Canvas1.Image == null)
            {
                AppDialog.Show(this, "请先打开一张图片。", "提示");
                return;
            }

            string tag = null;
            if (sender is FrameworkElement element)
                tag = element.Tag as string;
            if (!Enum.TryParse(tag, out ImageTransformOperation operation))
                return;

            ApplyImageTransformCommand(operation);
        }

        private void ApplyImageTransformCommand(ImageTransformOperation operation)
        {
            if (Canvas1.Image == null) return;
            RotatePopup.IsOpen = false;
            SavePopup.IsOpen = false;
            CaptureHistoryVersion(GetImageTransformHistoryName(operation));
            if (!Canvas1.ApplyImageTransform(operation)) return;

            RecordRecentContextAction(operation.ToString());
            _fitToWindow = true;
            FitImageAfterLayout();
            UpdateImageInfo(Canvas1.Image, GetCurrentImageFileSize());
            UpdateStatus(GetImageTransformStatus(operation));
        }

        private void ContextCrop_Click(object sender, RoutedEventArgs e)
        {
            if (Canvas1.Image == null) return;
            SetEditMode(true, false);
            SetActiveTool("Crop");
            RecordRecentContextAction("Crop");
            UpdateStatus("已进入裁剪模式，拖动边框调整范围。");
        }

        private void ImageStage_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            bool hasImage = Canvas1.Image != null;
            foreach (var item in new[] { CtxCopyImage, CtxSaveAs, CtxPrint, CtxRotate, CtxFlip, CtxCrop, CtxFormatConvert, CtxOpenLocation, CtxOpenWith, CtxImageInfo })
            {
                if (item != null) item.IsEnabled = hasImage;
            }
            if (CtxOpenLocation != null)
                CtxOpenLocation.IsEnabled = hasImage && !string.IsNullOrWhiteSpace(GetCurrentDiskPath());
            if (CtxOpenWith != null)
                CtxOpenWith.IsEnabled = hasImage && !string.IsNullOrWhiteSpace(GetCurrentDiskPath());
            ApplyContextMenuTheme(_editMode);
            UpdateRecentContextActionUi();
        }

        private void MenuOpenContainingFolder_Click(object sender, RoutedEventArgs e)
        {
            if (OptionsPopup != null)
                OptionsPopup.IsOpen = false;

            string path = GetCurrentDiskPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                AppDialog.Show(this, "当前图片还没有可定位的磁盘文件。", "提示");
                return;
            }

            Process.Start("explorer.exe", $"/select,\"{path}\"");
            RecordRecentContextAction("OpenLocation");
        }

        private void OptOpenWith_Click(object sender, RoutedEventArgs e)
        {
            string path = GetCurrentDiskPath();
            if (!EnsureCanOpenWith(path)) return;

            // 顶部选项里使用独立的下拉菜单；右键菜单则使用真正的二级菜单。
            var menu = CreateOpenWithMenu(path);
            menu.PlacementTarget = BtnOptions;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.HorizontalOffset = -152;
            menu.VerticalOffset = 4;
            menu.IsOpen = true;
        }

        private void CtxOpenWith_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            var menu = sender as MenuItem;
            if (menu == null) return;

            string path = GetCurrentDiskPath();
            PopulateOpenWithMenu(menu, path);
        }

        private ContextMenu CreateOpenWithMenu(string path)
        {
            var menu = new ContextMenu
            {
                StaysOpen = false
            };
            PopulateOpenWithMenu(menu, path);
            return menu;
        }

        private void PopulateOpenWithMenu(ItemsControl menu, string path)
        {
            menu.Items.Clear();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                menu.Items.Add(new MenuItem { Header = "请先把图片保存为文件", IsEnabled = false });
                return;
            }

            foreach (OpenWithApplication application in GetOpenWithApplications(path))
            {
                var item = new MenuItem
                {
                    Header = application.DisplayName,
                    Tag = application,
                    ToolTip = application.ExecutablePath
                };
                item.Click += OpenWithApplication_Click;
                menu.Items.Add(item);
            }

            if (menu.Items.Count == 0)
                menu.Items.Add(new MenuItem { Header = "没有找到已注册的图片应用", IsEnabled = false });

            menu.Items.Add(new Separator());
            var chooseOther = new MenuItem { Header = "选择其他应用..." };
            chooseOther.Click += MenuOpenWith_Click;
            menu.Items.Add(chooseOther);
        }

        private bool EnsureCanOpenWith(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return true;

            AppDialog.Show(this, "当前图片尚未保存为磁盘文件，请先另存为后再用其他应用打开。", "提示");
            return false;
        }

        private void OpenWithApplication_Click(object sender, RoutedEventArgs e)
        {
            var item = sender as MenuItem;
            var application = item?.Tag as OpenWithApplication;
            string path = GetCurrentDiskPath();
            if (application == null || !EnsureCanOpenWith(path)) return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = application.ExecutablePath,
                    Arguments = BuildOpenWithArguments(application.Arguments, path),
                    UseShellExecute = true
                });
                RecordRecentContextAction("OpenWith");
            }
            catch
            {
                AppDialog.Show(this, "无法用“" + application.DisplayName + "”打开当前图片。", "提示");
            }
        }

        private void MenuOpenWith_Click(object sender, RoutedEventArgs e)
        {
            if (OptionsPopup != null)
                OptionsPopup.IsOpen = false;

            string path = GetCurrentDiskPath();
            if (!EnsureCanOpenWith(path)) return;

            try
            {
                string openWithExe = Path.Combine(Environment.SystemDirectory, "OpenWith.exe");
                if (File.Exists(openWithExe))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = openWithExe,
                        Arguments = QuoteOpenWithArgument(path),
                        UseShellExecute = true
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "rundll32.exe",
                        Arguments = "shell32.dll,OpenAs_RunDLL \"" + path + "\"",
                        UseShellExecute = true
                    });
                }
                RecordRecentContextAction("OpenWith");
            }
            catch
            {
                AppDialog.Show(this, "无法打开系统的“选择其他应用”窗口。", "提示");
            }
        }

        private static string BuildOpenWithArguments(string registeredArguments, string filePath)
        {
            string quotedPath = QuoteOpenWithArgument(filePath);
            string arguments = registeredArguments ?? string.Empty;
            bool hasFilePlaceholder = arguments.IndexOf("%1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      arguments.IndexOf("%l", StringComparison.OrdinalIgnoreCase) >= 0;

            // 注册表命令常写成 "%1"。先替换带引号的形式，避免出现双引号。
            arguments = arguments.Replace("\"%1\"", quotedPath)
                                 .Replace("\"%L\"", quotedPath)
                                 .Replace("\"%l\"", quotedPath)
                                 .Replace("%1", quotedPath)
                                 .Replace("%L", quotedPath)
                                 .Replace("%l", quotedPath);
            return hasFilePlaceholder ? arguments.Trim() : (arguments + " " + quotedPath).Trim();
        }

        private static string QuoteOpenWithArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private List<OpenWithApplication> GetOpenWithApplications(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            var applications = new List<OpenWithApplication>();
            var seenExecutablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var programIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var executableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            CollectRegisteredProgramIds(extension, programIds);
            CollectRegisteredOpenWithExecutables(extension, executableNames);

            foreach (string programId in programIds)
                AddApplicationForProgramId(applications, seenExecutablePaths, programId);
            foreach (string executableName in executableNames)
                AddApplicationForExecutableName(applications, seenExecutablePaths, executableName);

            AddApplicationsSupportingExtension(applications, seenExecutablePaths, extension);
            AddKnownImageApplicationsFromAppPaths(applications, seenExecutablePaths);

            return applications
                .OrderBy(application => GetOpenWithApplicationRank(application.DisplayName))
                .ThenBy(application => application.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Take(14)
                .ToList();
        }

        private static void CollectRegisteredProgramIds(string extension, ISet<string> programIds)
        {
            AddRegistryDefaultValue(Registry.ClassesRoot, extension, programIds);
            AddRegistryValueNames(Registry.ClassesRoot, extension + "\\OpenWithProgids", programIds);
            AddRegistryValueNames(Registry.ClassesRoot, "SystemFileAssociations\\" + extension + "\\OpenWithProgids", programIds);

            try
            {
                using (RegistryKey userChoice = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\FileExts\\" + extension + "\\UserChoice"))
                {
                    string programId = userChoice?.GetValue("ProgId") as string;
                    if (!string.IsNullOrWhiteSpace(programId)) programIds.Add(programId);
                }
            }
            catch
            {
                // 访问受策略限制时，仍可从 HKCR 的常规关联继续读取。
            }
        }

        private static void CollectRegisteredOpenWithExecutables(string extension, ISet<string> executableNames)
        {
            AddOpenWithListEntries(Registry.ClassesRoot, extension + "\\OpenWithList", executableNames);
            AddOpenWithListEntries(Registry.ClassesRoot, "SystemFileAssociations\\" + extension + "\\OpenWithList", executableNames);
        }

        private static void AddRegistryDefaultValue(RegistryKey root, string path, ISet<string> values)
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(path))
                {
                    string value = key?.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
                }
            }
            catch { }
        }

        private static void AddRegistryValueNames(RegistryKey root, string path, ISet<string> values)
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(path))
                {
                    if (key == null) return;
                    foreach (string name in key.GetValueNames())
                    {
                        if (!string.IsNullOrWhiteSpace(name)) values.Add(name);
                    }
                }
            }
            catch { }
        }

        private static void AddOpenWithListEntries(RegistryKey root, string path, ISet<string> executableNames)
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(path))
                {
                    if (key == null) return;
                    foreach (string name in key.GetSubKeyNames())
                    {
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) executableNames.Add(name);
                    }
                    foreach (string valueName in key.GetValueNames())
                    {
                        string value = key.GetValue(valueName) as string;
                        if (!string.IsNullOrWhiteSpace(value) && value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            executableNames.Add(value);
                    }
                }
            }
            catch { }
        }

        private static void AddApplicationForProgramId(List<OpenWithApplication> applications, ISet<string> seenExecutablePaths, string programId)
        {
            if (string.IsNullOrWhiteSpace(programId)) return;
            try
            {
                using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(programId))
                {
                    AddApplicationFromRegistryKey(applications, seenExecutablePaths, key, programId);
                }
            }
            catch { }
        }

        private static void AddApplicationForExecutableName(List<OpenWithApplication> applications, ISet<string> seenExecutablePaths, string executableName)
        {
            if (string.IsNullOrWhiteSpace(executableName)) return;
            try
            {
                using (RegistryKey key = Registry.ClassesRoot.OpenSubKey("Applications\\" + executableName))
                {
                    if (key != null)
                    {
                        AddApplicationFromRegistryKey(applications, seenExecutablePaths, key, Path.GetFileNameWithoutExtension(executableName));
                        return;
                    }
                }
            }
            catch { }

            string appPath = GetApplicationPath(executableName);
            AddOpenWithApplication(applications, seenExecutablePaths, appPath, Path.GetFileNameWithoutExtension(executableName));
        }

        private static void AddApplicationFromRegistryKey(List<OpenWithApplication> applications, ISet<string> seenExecutablePaths, RegistryKey key, string fallbackName)
        {
            if (key == null) return;
            try
            {
                string friendlyName = key.GetValue("FriendlyAppName") as string;
                string command = GetOpenCommand(key);
                AddOpenWithApplication(applications, seenExecutablePaths, command, friendlyName ?? fallbackName);
            }
            catch { }
        }

        private static string GetOpenCommand(RegistryKey key)
        {
            try
            {
                using (RegistryKey commandKey = key.OpenSubKey("shell\\open\\command"))
                {
                    return commandKey?.GetValue(null) as string;
                }
            }
            catch
            {
                return null;
            }
        }

        private static void AddApplicationsSupportingExtension(List<OpenWithApplication> applications, ISet<string> seenExecutablePaths, string extension)
        {
            try
            {
                using (RegistryKey applicationsKey = Registry.ClassesRoot.OpenSubKey("Applications"))
                {
                    if (applicationsKey == null) return;
                    foreach (string executableName in applicationsKey.GetSubKeyNames())
                    {
                        using (RegistryKey applicationKey = applicationsKey.OpenSubKey(executableName))
                        using (RegistryKey supportedTypes = applicationKey?.OpenSubKey("SupportedTypes"))
                        {
                            if (supportedTypes == null || !supportedTypes.GetValueNames().Any(name => string.Equals(name, extension, StringComparison.OrdinalIgnoreCase)))
                                continue;
                            AddApplicationFromRegistryKey(applications, seenExecutablePaths, applicationKey, Path.GetFileNameWithoutExtension(executableName));
                        }
                    }
                }
            }
            catch
            {
                // 第三方程序通常会走 OpenWithList / OpenWithProgids；扫描失败不影响这些来源。
            }
        }

        private static void AddKnownImageApplicationsFromAppPaths(List<OpenWithApplication> applications, ISet<string> seenExecutablePaths)
        {
            AddKnownImageApplicationsFromAppPaths(applications, seenExecutablePaths, Registry.CurrentUser);
            AddKnownImageApplicationsFromAppPaths(applications, seenExecutablePaths, Registry.LocalMachine);
            try
            {
                using (RegistryKey localMachine32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                    AddKnownImageApplicationsFromAppPaths(applications, seenExecutablePaths, localMachine32);
            }
            catch { }
        }

        private static void AddKnownImageApplicationsFromAppPaths(List<OpenWithApplication> applications, ISet<string> seenExecutablePaths, RegistryKey root)
        {
            try
            {
                using (RegistryKey appPathsKey = root.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\App Paths"))
                {
                    if (appPathsKey == null) return;
                    foreach (string executableName in appPathsKey.GetSubKeyNames())
                    {
                        if (!IsLikelyImageApplication(executableName)) continue;
                        using (RegistryKey appPathKey = appPathsKey.OpenSubKey(executableName))
                        {
                            string executablePath = appPathKey?.GetValue(null) as string;
                            AddOpenWithApplication(applications, seenExecutablePaths, executablePath, Path.GetFileNameWithoutExtension(executableName));
                        }
                    }
                }
            }
            catch { }
        }

        private static string GetApplicationPath(string executableName)
        {
            string registryPath = "Software\\Microsoft\\Windows\\CurrentVersion\\App Paths\\" + executableName;
            foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using (RegistryKey key = root.OpenSubKey(registryPath))
                    {
                        string path = key?.GetValue(null) as string;
                        if (!string.IsNullOrWhiteSpace(path)) return path;
                    }
                }
                catch { }
            }
            return null;
        }

        private static bool IsLikelyImageApplication(string executableName)
        {
            string name = executableName.ToLowerInvariant();
            string[] imageApplicationNames =
            {
                "photoshop", "lightroom", "gimp", "krita", "paint", "affinity", "captureone",
                "xnview", "irfanview", "faststone", "cspaint", "clipstudio", "medibang", "sai", "canva"
            };
            return imageApplicationNames.Any(name.Contains);
        }

        private static void AddOpenWithApplication(List<OpenWithApplication> applications, ISet<string> seenExecutablePaths, string command, string fallbackName)
        {
            string executablePath;
            string arguments;
            if (!TrySplitOpenCommand(command, out executablePath, out arguments)) return;
            if (IsCurrentApplication(executablePath) || !seenExecutablePaths.Add(executablePath)) return;

            applications.Add(new OpenWithApplication
            {
                DisplayName = GetOpenWithDisplayName(executablePath, fallbackName),
                ExecutablePath = executablePath,
                Arguments = arguments
            });
        }

        private static bool TrySplitOpenCommand(string command, out string executablePath, out string arguments)
        {
            executablePath = null;
            arguments = string.Empty;
            if (string.IsNullOrWhiteSpace(command)) return false;

            string expandedCommand = Environment.ExpandEnvironmentVariables(command.Trim());
            if (expandedCommand.StartsWith("\"", StringComparison.Ordinal))
            {
                int endQuote = expandedCommand.IndexOf('"', 1);
                if (endQuote <= 1) return false;
                executablePath = expandedCommand.Substring(1, endQuote - 1);
                arguments = expandedCommand.Substring(endQuote + 1).Trim();
            }
            else
            {
                int executableEnd = expandedCommand.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (executableEnd < 0) return false;
                executablePath = expandedCommand.Substring(0, executableEnd + 4).Trim();
                arguments = expandedCommand.Substring(executableEnd + 4).Trim();
            }

            executablePath = executablePath.Trim().Trim('"');
            return File.Exists(executablePath);
        }

        private static bool IsCurrentApplication(string executablePath)
        {
            try
            {
                return string.Equals(executablePath, Assembly.GetEntryAssembly().Location, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string GetOpenWithDisplayName(string executablePath, string fallbackName)
        {
            try
            {
                FileVersionInfo version = FileVersionInfo.GetVersionInfo(executablePath);
                if (!string.IsNullOrWhiteSpace(version.ProductName)) return version.ProductName;
                if (!string.IsNullOrWhiteSpace(version.FileDescription)) return version.FileDescription;
            }
            catch { }
            return !string.IsNullOrWhiteSpace(fallbackName) ? fallbackName : Path.GetFileNameWithoutExtension(executablePath);
        }

        private static int GetOpenWithApplicationRank(string displayName)
        {
            return IsLikelyImageApplication(displayName) ? 0 : 1;
        }

        private void MenuImageInfo_Click(object sender, RoutedEventArgs e)
        {
            if (OptionsPopup != null)
                OptionsPopup.IsOpen = false;

            if (Canvas1.Image == null)
            {
                AppDialog.Show(this, "请先打开一张图片。", "图片信息");
                return;
            }

            string path = GetCurrentDiskPath();
            string name = !string.IsNullOrWhiteSpace(path) ? Path.GetFileName(path) : "未保存图片";
            string location = !string.IsNullOrWhiteSpace(path) ? path : "尚未保存到磁盘";
            long? bytes = !string.IsNullOrWhiteSpace(path) ? TryGetFileSize(path) : null;
            string sizeText = bytes.HasValue ? FormatFileSize(bytes.Value) : "未知";
            string message =
                $"文件：{name}\n" +
                $"分辨率：{Canvas1.Image.PixelWidth}×{Canvas1.Image.PixelHeight}\n" +
                $"比例：{FormatAspectRatio(Canvas1.Image.PixelWidth, Canvas1.Image.PixelHeight)}\n" +
                $"体积：{sizeText}\n" +
                $"位置：{location}";
            AppDialog.Show(this, message, "图片信息");
            RecordRecentContextAction("ImageInfo");
        }

        private void MenuFormatConvert_Click(object sender, RoutedEventArgs e)
        {
            if (OptionsPopup != null)
                OptionsPopup.IsOpen = false;
            string currentPath = GetCurrentDiskPath();
            bool hasConvertibleDiskFile = !string.IsNullOrWhiteSpace(currentPath)
                && File.Exists(currentPath)
                && ImageConversionService.IsSupportedInput(currentPath);
            var initialFiles = hasConvertibleDiskFile
                ? new[] { currentPath }
                : new string[0];
            string initialDir = !string.IsNullOrWhiteSpace(currentPath)
                ? Path.GetDirectoryName(currentPath)
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            BitmapSource currentBitmap = hasConvertibleDiskFile || Canvas1.Image == null
                ? null
                : Canvas1.RenderFullResolution();
            string currentName = string.IsNullOrWhiteSpace(currentPath)
                ? (CurrentFileText?.ToolTip as string ?? "PicMark 图片")
                : Path.GetFileName(currentPath);
            string currentExtension = hasConvertibleDiskFile
                ? Path.GetExtension(currentPath)
                : ".png";

            var window = new FormatConvertWindow(initialFiles, initialDir, currentBitmap, currentName, currentExtension)
            {
                Owner = this
            };
            window.ShowDialog();
            RecordRecentContextAction("FormatConvert");
        }

        private void RecentContextAction_Click(object sender, RoutedEventArgs e)
        {
            string actionId = CtxRecentAction?.Tag as string;
            if (string.IsNullOrWhiteSpace(actionId))
                actionId = _settings.RecentContextAction;
            ExecuteRecentContextAction(actionId);
        }

        private void ExecuteRecentContextAction(string actionId)
        {
            if (Canvas1.Image == null || string.IsNullOrWhiteSpace(actionId)) return;

            if (Enum.TryParse(actionId, out ImageTransformOperation operation))
            {
                ApplyImageTransformCommand(operation);
                return;
            }

            switch (actionId)
            {
                case "CopyImage":
                    MenuCopy_Click(this, null);
                    break;
                case "SaveAs":
                    MenuSaveAs_Click(this, null);
                    break;
                case "Print":
                    BtnPrint_Click(this, null);
                    break;
                case "Crop":
                    ContextCrop_Click(this, null);
                    break;
                case "OpenLocation":
                    MenuOpenContainingFolder_Click(this, null);
                    break;
                case "OpenWith":
                    MenuOpenWith_Click(this, null);
                    break;
                case "ImageInfo":
                    MenuImageInfo_Click(this, null);
                    break;
                case "FormatConvert":
                    MenuFormatConvert_Click(this, null);
                    break;
            }
        }

        private void RecordRecentContextAction(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId) || GetRecentContextActionLabel(actionId) == null) return;
            if (_settings.RecentContextAction == actionId)
            {
                UpdateRecentContextActionUi();
                return;
            }

            _settings.RecentContextAction = actionId;
            _settings.Save();
            UpdateRecentContextActionUi();
        }

        private void UpdateRecentContextActionUi()
        {
            if (CtxRecentAction == null) return;

            string actionId = _settings?.RecentContextAction;
            string label = GetRecentContextActionLabel(actionId);
            bool enabled = Canvas1?.Image != null && label != null && IsRecentContextActionAvailable(actionId);
            CtxRecentAction.Tag = actionId;
            CtxRecentAction.Header = label == null ? "暂无记录" : "再次执行：" + label;
            CtxRecentAction.IsEnabled = enabled;
            if (CtxRecentHeader != null) CtxRecentHeader.IsEnabled = false;
        }

        private bool IsRecentContextActionAvailable(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId)) return false;
            if (actionId == "OpenLocation" || actionId == "OpenWith")
                return !string.IsNullOrWhiteSpace(GetCurrentDiskPath());
            return GetRecentContextActionLabel(actionId) != null;
        }

        private static string GetRecentContextActionLabel(string actionId)
        {
            switch (actionId)
            {
                case "CopyImage":
                    return "复制图片";
                case "SaveAs":
                    return "另存为";
                case "Print":
                    return "打印";
                case "Crop":
                    return "裁剪图片";
                case "OpenLocation":
                    return "打开所在位置";
                case "OpenWith":
                    return "用其他应用打开";
                case "ImageInfo":
                    return "图片信息";
                case "FormatConvert":
                    return "另存为其他格式";
                case "RotateLeft90":
                    return "向左旋转 90°";
                case "RotateRight90":
                    return "向右旋转 90°";
                case "Rotate180":
                    return "旋转 180°";
                case "FlipHorizontal":
                    return "水平翻转";
                case "FlipVertical":
                    return "垂直翻转";
                default:
                    return null;
            }
        }

        private string GetCurrentDiskPath()
        {
            if (!string.IsNullOrWhiteSpace(_currentFilePath) && File.Exists(_currentFilePath))
                return _currentFilePath;
            if (!string.IsNullOrWhiteSpace(_currentProjectPath) && File.Exists(_currentProjectPath))
                return _currentProjectPath;
            return null;
        }

        private static string GetImageTransformHistoryName(ImageTransformOperation operation)
        {
            switch (operation)
            {
                case ImageTransformOperation.RotateLeft90:
                    return "向左旋转";
                case ImageTransformOperation.RotateRight90:
                    return "向右旋转";
                case ImageTransformOperation.Rotate180:
                    return "旋转 180°";
                case ImageTransformOperation.FlipHorizontal:
                    return "水平翻转";
                case ImageTransformOperation.FlipVertical:
                    return "垂直翻转";
                default:
                    return "变换图片";
            }
        }

        private static string GetImageTransformStatus(ImageTransformOperation operation)
        {
            switch (operation)
            {
                case ImageTransformOperation.RotateLeft90:
                    return "已向左旋转 90°。";
                case ImageTransformOperation.RotateRight90:
                    return "已向右旋转 90°。";
                case ImageTransformOperation.Rotate180:
                    return "已旋转 180°。";
                case ImageTransformOperation.FlipHorizontal:
                    return "已水平翻转。";
                case ImageTransformOperation.FlipVertical:
                    return "已垂直翻转。";
                default:
                    return "已变换图片。";
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnOverwrite_Click(object sender, RoutedEventArgs e)
        {
            if (_currentFilePath == null)
            {
                AppDialog.Show(this, "这张图片还没有对应的原文件路径，无法覆盖，请用“保存”另存为新文件。", "提示");
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

            try
            {
                var rendered = Canvas1.RenderFullResolution();

                if (overwrite && _currentFilePath != null)
                {
                    targetPath = _currentFilePath;
                    SaveBitmapWithOptions(rendered, targetPath, ext, rendered.PixelWidth, rendered.PixelHeight, 95, null);
                    _hasUnsavedChanges = false;
                    UpdateStatus($"已保存（已覆盖原图）：{targetPath}");
                    SaveCurrentWatermarkPresetToHistory();
                return true;
                }

                var options = new SaveOptionsDialog(rendered, _currentFilePath, ext)
                {
                    Owner = this
                };
                if (options.ShowDialog() != true) return false;

                targetPath = options.TargetPath;
                SaveBitmapWithOptions(rendered, targetPath, options.TargetExtension, options.OutputWidth, options.OutputHeight, options.Quality, options.TargetBytes);
                _hasUnsavedChanges = false;
                UpdateStatus($"已保存：{targetPath}（原图未改动）");
                SaveCurrentWatermarkPresetToHistory();
                return true;
            }
            catch (Exception ex)
            {
                string message = BuildSaveFailureMessage(ex);
                UpdateStatus($"保存失败：{ex.Message}");
                AppDialog.Show(this, message, "保存失败");
                return false;
            }
        }

        private bool SaveProject(bool saveAs)
        {
            if (Canvas1.Image == null)
            {
                AppDialog.Show(this, "请先打开一张图片。", "提示");
                return false;
            }

            string targetPath = _currentProjectPath;
            if (saveAs || string.IsNullOrEmpty(targetPath))
            {
                string baseDir = _currentFilePath != null
                    ? Path.GetDirectoryName(_currentFilePath)
                    : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string baseName = _currentFilePath != null
                    ? Path.GetFileNameWithoutExtension(_currentFilePath)
                    : "PicMark项目";
                var dlg = new SaveFileDialog
                {
                    Title = "保存 PicMark 项目",
                    Filter = "PicMark 项目|*.picmark",
                    InitialDirectory = baseDir,
                    FileName = baseName + ProjectExtension,
                    DefaultExt = ProjectExtension,
                    AddExtension = true,
                    OverwritePrompt = true
                };
                if (dlg.ShowDialog(this) != true) return false;
                targetPath = dlg.FileName;
            }

            try
            {
                SaveProjectToPath(targetPath);
                _currentProjectPath = targetPath;
                _hasUnsavedChanges = false;
                AddRecentFile(targetPath);
                SetCurrentFileName(Path.GetFileName(targetPath));
                UpdateStatus($"已保存项目：{targetPath}");
                return true;
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, $"保存项目失败：{ex.Message}", "提示");
                return false;
            }
        }

        private void SaveProjectToPath(string path)
        {
            string sourceName = _currentFilePath != null
                ? Path.GetFileName(_currentFilePath)
                : Path.GetFileName(path);
            ProjectStore.Save(path, Canvas1.Image, sourceName, Canvas1.Annotations, Canvas1.GetWatermark());
        }

        private static void SaveBitmapWithOptions(BitmapSource source, string targetPath, string ext, int width, int height, int quality, long? targetBytes)
        {
            BitmapSource resized = ResizeBitmap(source, width, height);
            byte[] bytes = EncodeBitmap(resized, ext, quality);

            if (targetBytes.HasValue)
            {
                if (ext == ".jpg" || ext == ".jpeg" || ext == ".webp")
                {
                int low = ext == ".webp" ? 20 : 35;
                int high = Math.Max(low, Math.Min(quality, 98));
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

        private static string BuildSaveFailureMessage(Exception ex)
        {
            string hint = "请检查磁盘空间是否充足，目标文件夹是否可写，或换一个位置保存。";
            if (ex is UnauthorizedAccessException)
                hint = "目标位置没有写入权限，请换到桌面、文档等可写目录。";
            else if (ex is IOException)
                hint = "目标文件可能正在被其他程序占用，或磁盘空间不足。";
            else if (ex is OutOfMemoryException)
                hint = "当前图片太大或标注太复杂，内存不足。请先降低输出尺寸，或关闭其他程序后再试。";

            return $"保存失败：{ex.Message}\n\n{hint}";
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

        internal static byte[] EncodeBitmap(BitmapSource source, string ext, int quality)
        {
            ext = ImageConversionService.NormalizeExtension(ext);
            if (ext == ".webp")
                return ImageConversionService.EncodeBitmap(source, ".webp", quality);

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
            UpdateOptionsMenuAvailability();
            ApplyPopupTheme(_editMode);
            OptionsPopup.IsOpen = true;
        }

        private void UpdateOptionsMenuAvailability()
        {
            bool hasImage = Canvas1.Image != null;
            string path = GetCurrentDiskPath();
            bool hasDiskFile = !string.IsNullOrWhiteSpace(path) && File.Exists(path);

            if (OptImageInfo != null)
                OptImageInfo.IsEnabled = hasImage;
            if (OptOpenLocation != null)
                OptOpenLocation.IsEnabled = hasDiskFile;
            if (OptOpenWith != null)
                OptOpenWith.IsEnabled = hasDiskFile;
            if (OptFormatConvert != null)
                OptFormatConvert.IsEnabled = hasImage;
        }

        private void MenuOpenNewImage_Click(object sender, RoutedEventArgs e)
        {
            OptionsPopup.IsOpen = false;
            BtnOpen_Click(sender, e);
        }

        private void MenuShortcutsHelp_Click(object sender, RoutedEventArgs e)
        {
            OptionsPopup.IsOpen = false;
            ShowShortcutsDialog();
        }

        private void MenuDefaultAppsHelp_Click(object sender, RoutedEventArgs e)
        {
            OptionsPopup.IsOpen = false;
            var result = AppDialog.Show(this,
                "Windows 10 不允许应用静默修改默认图片打开方式。\n\n我可以帮你打开系统“默认应用”设置：在“照片查看器/按文件类型选择默认应用”里，把 jpg、png、bmp、webp 选择为 PicMark。",
                "设为默认打开方式",
                MessageBoxButton.OKCancel);
            if (result != MessageBoxResult.OK) return;

            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, $"无法打开系统默认应用设置：{ex.Message}", "提示");
            }
        }

        private void MenuReportIssue_Click(object sender, RoutedEventArgs e)
        {
            OptionsPopup.IsOpen = false;
            string report = BuildIssueReportTemplate();
            bool copied = TrySetClipboardText(report);
            AppDialog.Show(this,
                copied
                    ? "已把错误汇报模板复制到剪贴板。\n\n你可以直接粘贴给开发者，最好再附上一张截图和触发步骤。"
                    : "已生成错误汇报模板，但复制到剪贴板失败。\n\n请重试，或手动描述：截图、触发步骤、当前文件名、系统版本。",
                "汇报错误");
        }

        private string BuildIssueReportTemplate()
        {
            string appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            string file = !string.IsNullOrWhiteSpace(_currentFilePath) ? Path.GetFileName(_currentFilePath) : "未打开图片";
            string image = Canvas1?.Image == null
                ? "无"
                : $"{Canvas1.Image.PixelWidth}x{Canvas1.Image.PixelHeight}, 缩放 {Canvas1.Scale:P0}";
            return
                "PicMark 错误汇报\n" +
                $"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                $"版本：{appVersion}\n" +
                $"系统：{Environment.OSVersion.VersionString}\n" +
                $"当前文件：{file}\n" +
                $"图片信息：{image}\n\n" +
                "问题现象：\n" +
                "- \n\n" +
                "复现步骤：\n" +
                "1. \n" +
                "2. \n" +
                "3. \n\n" +
                "期望结果：\n" +
                "- \n\n" +
                "附件：请附截图/问题图片（如方便）。";
        }

        private static bool TrySetClipboardText(string text)
        {
            try
            {
                Clipboard.SetText(text ?? string.Empty);
                return true;
            }
            catch (COMException)
            {
                return false;
            }
            catch (ExternalException)
            {
                return false;
            }
        }

        private void MenuHistoryCache_Click(object sender, RoutedEventArgs e)
        {
            OptionsPopup.IsOpen = false;
            ShowHistoryCacheDialog();
        }

        private async void MenuCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            OptionsPopup.IsOpen = false;
            await CheckForUpdatesAsync(true);
        }

        private async void TitleUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesAsync(true);
        }

        private async Task CheckForUpdatesAsync(bool showDialog)
        {
            OptCheckUpdateText.Text = "正在检查...";
            UpdateTitleUpdateUi(true);
            UpdateCheckResult result = await OnlineServices.CheckForUpdateAsync(GetDisplayVersion(), _settings);
            HandleUpdateCheckResult(result, showDialog);
        }

        private void MenuUpdatePrivacy_Click(object sender, RoutedEventArgs e)
        {
            OptionsPopup.IsOpen = false;
            ShowUpdatePrivacyDialog();
        }

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            OptionsPopup.IsOpen = false;
            ShowAboutDialog();
        }

        private void StartOnlineServicesTimer()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            timer.Tick += async (s, e) =>
            {
                timer.Stop();
                _telemetryUrl = _settings.LastTelemetryUrl;

                UpdateCheckResult result = null;
                bool needsManifest = OnlineServices.ShouldCheckUpdate(_settings)
                    || (string.Equals(_settings.TelemetryConsent, "Allowed", StringComparison.OrdinalIgnoreCase)
                        && string.IsNullOrWhiteSpace(_telemetryUrl));
                if (needsManifest)
                {
                    result = await OnlineServices.CheckForUpdateAsync(GetDisplayVersion(), _settings);
                    HandleUpdateCheckResult(result, false);
                    if (_settings.AutoCheckUpdates && result != null && result.Success)
                        OnlineServices.MarkUpdateChecked(_settings);
                }

                _telemetryUrl = result?.TelemetryUrl ?? _telemetryUrl;
                await OnlineServices.SendDailyTelemetryAsync(_settings, GetDisplayVersion(), _telemetryUrl);
            };
            timer.Start();
        }

        private void HandleUpdateCheckResult(UpdateCheckResult result, bool showDialog)
        {
            _lastUpdateCheck = result;
            if (result != null && !string.IsNullOrWhiteSpace(result.TelemetryUrl))
            {
                _telemetryUrl = result.TelemetryUrl;
                _settings.LastTelemetryUrl = result.TelemetryUrl;
                _settings.Save();
            }

            if (result == null || !result.Success)
            {
                OptCheckUpdateText.Text = "检查更新";
                UpdateTitleUpdateUi();
                if (showDialog)
                    AppDialog.Show(this, result?.FailureMessage ?? "暂时无法检查更新，请稍后再试。", "检查更新");
                return;
            }

            if (!result.HasUpdate)
            {
                OptCheckUpdateText.Text = "检查更新";
                UpdateTitleUpdateUi();
                if (showDialog)
                    AppDialog.Show(this, $"当前已是最新版本：{GetDisplayVersion()}", "检查更新");
                return;
            }

            bool ignored = string.Equals(_settings.IgnoredUpdateVersion, result.LatestVersion, StringComparison.OrdinalIgnoreCase);
            OptCheckUpdateText.Text = ignored ? "检查更新" : "检查更新 · 有新版";
            UpdateTitleUpdateUi();

            if (showDialog)
                ShowUpdateAvailableDialog(result);
        }

        private void UpdateTitleUpdateUi(bool isChecking = false)
        {
            if (TitleVersionText != null)
                TitleVersionText.Text = "v" + GetDisplayVersion();
            if (TitleUpdateButton == null) return;

            Color buttonBackground = _editMode ? Color.FromRgb(0x32, 0x36, 0x3B) : Color.FromRgb(0xFF, 0xFF, 0xFF);
            Color buttonBorder = _editMode ? Color.FromRgb(0x4F, 0x56, 0x5E) : Color.FromRgb(0xD7, 0xDE, 0xE5);
            Color neutralForeground = _editMode ? Color.FromRgb(0xE7, 0xEB, 0xEF) : Color.FromRgb(0x44, 0x57, 0x6A);
            if (isChecking)
            {
                SetTitleUpdateButtonAppearance("正在检查", "正在检查更新", false,
                    buttonBackground, buttonBorder, neutralForeground);
                return;
            }

            bool hasVisibleUpdate = _lastUpdateCheck != null && _lastUpdateCheck.Success && _lastUpdateCheck.HasUpdate
                && !string.Equals(_settings.IgnoredUpdateVersion, _lastUpdateCheck.LatestVersion, StringComparison.OrdinalIgnoreCase);
            if (hasVisibleUpdate)
            {
                SetTitleUpdateButtonAppearance("有新版本", "发现 v" + _lastUpdateCheck.LatestVersion + "，点击查看更新", true,
                    _editMode ? Color.FromRgb(0x3B, 0x43, 0x4D) : Color.FromRgb(0xF2, 0xF6, 0xFF),
                    _editMode ? Color.FromRgb(0x5E, 0x69, 0x76) : Color.FromRgb(0xC8, 0xD6, 0xF2),
                    _editMode ? Color.FromRgb(0xE5, 0xEC, 0xF7) : Color.FromRgb(0x3E, 0x62, 0xA8));
                return;
            }

            SetTitleUpdateButtonAppearance("检查更新", "检查是否有新版本", true,
                buttonBackground, buttonBorder, neutralForeground);
        }

        private void SetTitleUpdateButtonAppearance(string content, string toolTip, bool isEnabled, Color background, Color border, Color foreground)
        {
            TitleUpdateButton.Content = content;
            TitleUpdateButton.ToolTip = toolTip;
            TitleUpdateButton.IsEnabled = isEnabled;
            TitleUpdateButton.Background = new SolidColorBrush(background);
            TitleUpdateButton.BorderBrush = new SolidColorBrush(border);
            TitleUpdateButton.Foreground = new SolidColorBrush(foreground);
        }

        private void ShowUpdateAvailableDialog(UpdateCheckResult result)
        {
            var dialog = CreateSimpleDialog("发现新版本", 520, 380);
            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(MakeDialogTitle($"发现新版本 {result.LatestVersion}"));
            root.Children.Add(new TextBlock
            {
                Text = string.Equals(result.SourceName, "国内镜像", StringComparison.Ordinal)
                    ? "GitHub 暂时无法访问，本次已从国内镜像读取更新。更新不会自动安装，请选择下载方式。"
                    : "更新不会自动安装，请选择下载方式。",
                Foreground = DialogSecondaryTextBrush(),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Margin = new Thickness(0, 0, 0, 12)
            });

            if (result.Notes != null && result.Notes.Count > 0)
            {
                foreach (string note in result.Notes.Take(5))
                    root.Children.Add(MakeDialogText("• " + note));
            }

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var ignoreButton = new Button { Content = "不再提醒此版本", Style = (Style)FindResource("ToolButton"), MinWidth = 118, Margin = new Thickness(0, 0, 8, 0) };
            var laterButton = new Button { Content = "稍后再说", Style = (Style)FindResource("ToolButton"), MinWidth = 84, Margin = new Thickness(0, 0, 8, 0) };
            var portableButton = new Button { Content = "免安装版", Style = (Style)FindResource("ToolButton"), MinWidth = 88, Margin = new Thickness(0, 0, 8, 0), IsEnabled = !string.IsNullOrWhiteSpace(result.PortableUrl) };
            var setupButton = new Button { Content = "下载安装版", Style = (Style)FindResource("PrimaryButton"), MinWidth = 96, IsEnabled = !string.IsNullOrWhiteSpace(result.SetupUrl) };
            buttons.Children.Add(ignoreButton);
            buttons.Children.Add(laterButton);
            buttons.Children.Add(portableButton);
            buttons.Children.Add(setupButton);
            root.Children.Add(buttons);

            ignoreButton.Click += (s, e) =>
            {
                _settings.IgnoredUpdateVersion = result.LatestVersion ?? string.Empty;
                _settings.Save();
                OptCheckUpdateText.Text = "检查更新";
                UpdateTitleUpdateUi();
                dialog.Close();
            };
            laterButton.Click += (s, e) => dialog.Close();
            portableButton.Click += (s, e) =>
            {
                OpenExternalUrl(result.PortableUrl);
                dialog.Close();
            };
            setupButton.Click += (s, e) =>
            {
                OpenExternalUrl(result.SetupUrl);
                dialog.Close();
            };

            dialog.Content = root;
            dialog.ShowDialog();
        }


        private void ShowUpdatePrivacyDialog()
        {
            var dialog = CreateSimpleDialog("更新与隐私", 560, 430);
            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(MakeDialogTitle("更新与隐私"));

            var autoUpdate = new CheckBox
            {
                Content = "自动检查更新（启动后延迟检查，最多每 3 天一次）",
                IsChecked = _settings.AutoCheckUpdates,
                Foreground = DialogPrimaryTextBrush(),
                Margin = new Thickness(0, 8, 0, 10)
            };
            var telemetry = new CheckBox
            {
                Content = "发送匿名启动与兼容性信息（每天最多一次，可随时关闭）",
                IsChecked = string.Equals(_settings.TelemetryConsent, "Allowed", StringComparison.OrdinalIgnoreCase),
                Foreground = DialogPrimaryTextBrush(),
                Margin = new Thickness(0, 0, 0, 12)
            };
            root.Children.Add(autoUpdate);
            root.Children.Add(telemetry);
            root.Children.Add(new TextBlock
            {
                Text = "仅包含软件版本、安装版/免安装版、Windows 版本、屏幕分辨率区间、随机匿名 ID 和日期。\n\n不会上传图片、文件名、文件路径、编辑内容、Windows 用户名或电脑名。没有网络或服务器不可用时会静默失败。",
                Foreground = DialogSecondaryTextBrush(),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Margin = new Thickness(0, 0, 0, 18)
            });

            string current = _lastUpdateCheck != null && _lastUpdateCheck.Success
                ? (_lastUpdateCheck.HasUpdate ? $"发现新版：{_lastUpdateCheck.LatestVersion}" : "当前已是最新版本")
                : "尚未手动检查更新";
            root.Children.Add(MakeDialogText("更新状态：" + current));

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var checkButton = new Button { Content = "立即检查", Style = (Style)FindResource("ToolButton"), MinWidth = 84, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 4, 10, 4), Background = DialogSecondaryButtonBrush(), BorderBrush = DialogBorderBrush(), BorderThickness = new Thickness(1), Foreground = DialogPrimaryTextBrush() };
            var privacyButton = new Button { Content = "隐私说明", Style = (Style)FindResource("ToolButton"), MinWidth = 84, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 4, 10, 4), Background = DialogSecondaryButtonBrush(), BorderBrush = DialogBorderBrush(), BorderThickness = new Thickness(1), Foreground = DialogPrimaryTextBrush() };
            var saveButton = new Button { Content = "保存", Style = (Style)FindResource("PrimaryButton"), MinWidth = 72 };
            buttons.Children.Add(checkButton);
            buttons.Children.Add(privacyButton);
            buttons.Children.Add(saveButton);
            root.Children.Add(buttons);

            checkButton.Click += async (s, e) =>
            {
                UpdateCheckResult result = await OnlineServices.CheckForUpdateAsync(GetDisplayVersion(), _settings);
                HandleUpdateCheckResult(result, true);
            };
            privacyButton.Click += (s, e) => OpenExternalUrl("https://github.com/Tsang12140/picmark/blob/main/docs/PRIVACY.md");
            saveButton.Click += (s, e) =>
            {
                _settings.AutoCheckUpdates = autoUpdate.IsChecked == true;
                _settings.TelemetryConsent = telemetry.IsChecked == true ? "Allowed" : "Denied";
                _settings.Save();
                dialog.Close();
            };

            dialog.Content = root;
            dialog.ShowDialog();
        }

        private Window CreateSimpleDialog(string title, double width, double height)
        {
            bool isLight = !_editMode;
            return new Window
            {
                Owner = this,
                Title = title,
                Width = width,
                Height = height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = (Brush)FindResource("PanelBrush"),
                Foreground = DialogPrimaryTextBrush(),
                FontFamily = UiFonts.Family,
                BorderBrush = isLight ? BrushFromRgb(0xC6, 0xD5, 0xE1) : BrushFromRgb(0x4C, 0x4C, 0x4C)
            };
        }

        private SolidColorBrush DialogPanelBrush() => _editMode
            ? BrushFromRgb(0x3A, 0x3A, 0x3A)
            : BrushFromRgb(0xF8, 0xFA, 0xFC);

        private SolidColorBrush DialogBorderBrush() => _editMode
            ? BrushFromRgb(0x4C, 0x4C, 0x4C)
            : BrushFromRgb(0xC6, 0xD5, 0xE1);

        private SolidColorBrush DialogPrimaryTextBrush() => _editMode
            ? BrushFromRgb(0xF4, 0xF4, 0xF5)
            : BrushFromRgb(0x1F, 0x2A, 0x37);

        private SolidColorBrush DialogSecondaryTextBrush() => _editMode
            ? BrushFromRgb(0xDA, 0xDE, 0xE6)
            : BrushFromRgb(0x4F, 0x5D, 0x6C);

        private SolidColorBrush DialogSecondaryButtonBrush() => _editMode
            ? BrushFromRgb(0x46, 0x46, 0x46)
            : BrushFromRgb(0xF7, 0xFA, 0xFC);

        private TextBlock MakeDialogTitle(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = DialogPrimaryTextBrush(),
                Margin = new Thickness(0, 0, 0, 14)
            };
        }

        private TextBlock MakeDialogText(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = DialogSecondaryTextBrush(),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Margin = new Thickness(0, 2, 0, 4)
            };
        }

        private void OpenExternalUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                AppDialog.Show(this, url, "请在浏览器中打开");
            }
        }

        private void ShowAboutDialog()
        {
            string version = GetDisplayVersion();

            var dialog = new Window
            {
                Title = "关于 PicMark",
                Owner = this,
                Width = 520,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                FontFamily = UiFonts.Family
            };

            var root = new Grid { Margin = new Thickness(24) };
            var panel = new Border
            {
                Background = DialogPanelBrush(),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(22, 18, 22, 22),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 24,
                    ShadowDepth = 5,
                    Opacity = _editMode ? 0.5 : 0.18
                }
            };

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var titleBar = new Grid { Margin = new Thickness(0, 0, 0, 18), Cursor = Cursors.SizeAll };
            titleBar.MouseLeftButtonDown += (s, args) =>
            {
                if (args.ClickCount == 1)
                {
                    try { dialog.DragMove(); }
                    catch (InvalidOperationException) { }
                }
            };

            titleBar.Children.Add(new TextBlock
            {
                Text = "关于 PicMark",
                Foreground = DialogPrimaryTextBrush(),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });

            var close = new Button
            {
                Content = "×",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 28,
                Height = 28,
                Background = Brushes.Transparent,
                Foreground = DialogSecondaryTextBrush(),
                BorderThickness = new Thickness(0),
                FontSize = 18,
                Cursor = Cursors.Hand
            };
            close.Click += (s, args) => dialog.Close();
            titleBar.Children.Add(close);
            Grid.SetRow(titleBar, 0);
            layout.Children.Add(titleBar);

            var content = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
            content.Children.Add(new TextBlock
            {
                Text = "见微 PicMark",
                Foreground = DialogPrimaryTextBrush(),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });
            content.Children.Add(new TextBlock
            {
                Text = "开源、本地优先的轻量图片查看与标注工具。",
                Foreground = DialogSecondaryTextBrush(),
                FontSize = 14,
                LineHeight = 22,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            });
            content.Children.Add(MakeAboutText("作者：Tsang12140"));
            content.Children.Add(MakeAboutText("许可证：MIT License"));

            var projectLine = MakeAboutText("项目地址：");
            var link = new Hyperlink(new Run("https://github.com/Tsang12140/picmark"))
            {
                NavigateUri = new Uri("https://github.com/Tsang12140/picmark"),
                Foreground = _editMode ? BrushFromRgb(132, 160, 255) : BrushFromRgb(60, 92, 160)
            };
            link.RequestNavigate += OpenAboutLink;
            projectLine.Inlines.Add(link);
            content.Children.Add(projectLine);

            content.Children.Add(MakeAboutText($"版本：{version}"));
            content.Children.Add(new TextBlock
            {
                Text = "PicMark 离线运行，不上传图片、文件名、文件路径或编辑内容；匿名统计需用户允许，且可在“更新与隐私”中关闭。\n默认保留原图，导出时才写入成品文件。\nCopyright © 2026 Tsang12140",
                Foreground = DialogSecondaryTextBrush(),
                FontSize = 14,
                LineHeight = 22,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 16, 0, 0)
            });
            Grid.SetRow(content, 1);
            layout.Children.Add(content);

            var ok = new Button
            {
                Content = "确认",
                MinWidth = 72,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Right,
                Padding = new Thickness(14, 0, 14, 0),
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(82, 101, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(82, 101, 255)),
                Foreground = Brushes.White
            };
            ok.Click += (s, args) => dialog.Close();
            Grid.SetRow(ok, 2);
            layout.Children.Add(ok);

            panel.Child = layout;
            root.Children.Add(panel);
            dialog.Content = root;
            dialog.ShowDialog();
        }

        private TextBlock MakeAboutText(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = DialogSecondaryTextBrush(),
                FontSize = 14,
                LineHeight = 22,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            };
        }

        private static void OpenAboutLink(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch
            {
                // Keep the about dialog quiet if Windows cannot open the default browser.
            }
            e.Handled = true;
        }

        private static string GetDisplayVersion()
        {
            string version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
                ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? "0.1.0";
            return version.EndsWith(".0", StringComparison.Ordinal)
                ? version.Substring(0, version.Length - 2)
                : version;
        }

        private void ShowShortcutsDialog()
        {
            var dialog = new Window
            {
                Title = "快捷键帮助",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 560,
                Height = 640,
                MinWidth = 500,
                MinHeight = 520,
                Background = DialogPanelBrush(),
                Foreground = DialogPrimaryTextBrush()
            };

            var root = new DockPanel { Margin = new Thickness(18) };
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            header.Children.Add(new TextBlock
            {
                Text = "快捷键",
                FontSize = 24,
                FontWeight = FontWeights.Bold
            });
            header.Children.Add(new TextBlock
            {
                Text = "高频操作放在最前面；文字输入时不会触发查看器快捷键。",
                Foreground = DialogSecondaryTextBrush(),
                Margin = new Thickness(0, 5, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var closeButton = new Button
            {
                Content = "关闭",
                Width = 88,
                Height = 34,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x52, 0x65, 0xFF)),
                Foreground = DialogPrimaryTextBrush(),
                BorderBrush = Brushes.Transparent,
                FontWeight = FontWeights.Bold
            };
            closeButton.Click += (s, args) => dialog.Close();
            DockPanel.SetDock(closeButton, Dock.Bottom);
            root.Children.Add(closeButton);

            var content = new StackPanel();
            AddShortcutSection(content, "高频操作",
                Tuple.Create("Ctrl + O", "打开图片"),
                Tuple.Create("Ctrl + S", "保存"),
                Tuple.Create("Ctrl + Shift + S", "另存为"),
                Tuple.Create("Ctrl + E", "切换查看 / 编辑"),
                Tuple.Create("Ctrl + C", "复制当前结果到剪贴板"),
                Tuple.Create("Ctrl + P", "打印"));
            AddShortcutSection(content, "查看",
                Tuple.Create("← / →", "上一张 / 下一张"),
                Tuple.Create("Ctrl + + / Ctrl + -", "放大 / 缩小"),
                Tuple.Create("Ctrl + 0", "适应窗口"),
                Tuple.Create("Ctrl + 1", "原始大小"),
                Tuple.Create("Ctrl + 滚轮", "连续缩放"),
                Tuple.Create("抓手按钮 + 拖动", "平移查看大图"));
            AddShortcutSection(content, "编辑",
                Tuple.Create("Ctrl + Z", "撤销"),
                Tuple.Create("Ctrl + Y", "重做"),
                Tuple.Create("Ctrl + Shift + Z", "重做"),
                Tuple.Create("Delete / Backspace", "删除选中的标注"),
                Tuple.Create("Ctrl + D", "取消选中"),
                Tuple.Create("Esc", "取消当前选中"));
            AddShortcutSection(content, "文字与裁剪",
                Tuple.Create("Enter", "完成文字编辑 / 确认裁剪"),
                Tuple.Create("Esc", "取消文字编辑 / 取消裁剪"),
                Tuple.Create("Backspace", "文字编辑时删除文字"));
            AddShortcutSection(content, "绘制辅助",
                Tuple.Create("Shift + 拖动", "圈选工具画正圆"),
                Tuple.Create("滚轮", "画笔、矩形、圆形、箭头工具下调整粗细"),
                Tuple.Create("滚轮", "马赛克工具下调整马赛克 / 高斯模糊强度"));

            var scroll = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            root.Children.Add(scroll);
            dialog.Content = root;
            dialog.ShowDialog();
        }

        private void AddShortcutSection(StackPanel root, string title, params Tuple<string, string>[] rows)
        {
            var card = new Border
            {
                Background = _editMode ? BrushFromRgb(0x32, 0x32, 0x32) : Brushes.White,
                BorderBrush = DialogBorderBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 10),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = DialogPrimaryTextBrush(),
                Margin = new Thickness(0, 0, 0, 8)
            });

            foreach (var row in rows)
            {
                var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var keyBadge = new Border
                {
                    Background = _editMode ? BrushFromRgb(0x3F, 0x3F, 0x3F) : BrushFromRgb(0xEC, 0xF2, 0xF7),
                    BorderBrush = DialogBorderBrush(),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 4, 8, 4),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    MinWidth = 86
                };
                keyBadge.Child = new TextBlock
                {
                    Text = row.Item1,
                    FontWeight = FontWeights.Bold,
                    Foreground = DialogPrimaryTextBrush()
                };
                Grid.SetColumn(keyBadge, 0);
                grid.Children.Add(keyBadge);

                var action = new TextBlock
                {
                    Text = row.Item2,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = DialogSecondaryTextBrush(),
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetColumn(action, 1);
                grid.Children.Add(action);
                stack.Children.Add(grid);
            }

            card.Child = stack;
            root.Children.Add(card);
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

        private void ApplyWatermarkAssetLimitSetting(string limitText)
        {
            if (!int.TryParse(limitText, out int limit)) limit = 12;
            limit = Math.Max(1, Math.Min(limit, 99));
            _settings.WatermarkAssetLimit = limit;
            WatermarkAssetManager.Touch(_watermarkLogoPath, _settings);
            WatermarkAssetManager.EnforceLimit(_settings);
            UpdateRecentLogoUi();
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
                Height = 310,
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
                Foreground = DialogPrimaryTextBrush(),
                Margin = new Thickness(0, 0, 0, 14)
            });
            root.Children.Add(new TextBlock
            {
                Text = $"当前约 {usedMb:0.#} MB / 上限 {_settings.HistoryCacheMb} MB",
                Foreground = DialogSecondaryTextBrush(),
                Margin = new Thickness(0, 0, 0, 8)
            });

            var box = new TextBox
            {
                Text = _settings.HistoryCacheMb.ToString(),
                Style = (Style)FindResource("PanelTextBox"),
                Margin = new Thickness(0, 0, 0, 14)
            };
            root.Children.Add(box);

            root.Children.Add(new TextBlock
            {
                Text = $"常用 Logo 素材上限（当前 {_settings.WatermarkLogoAssets.Count} / {_settings.WatermarkAssetLimit}）",
                Foreground = DialogSecondaryTextBrush(),
                Margin = new Thickness(0, 2, 0, 8)
            });

            var assetLimitBox = new TextBox
            {
                Text = _settings.WatermarkAssetLimit.ToString(),
                Style = (Style)FindResource("PanelTextBox"),
                Margin = new Thickness(0, 0, 0, 14)
            };
            root.Children.Add(assetLimitBox);

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
                ApplyWatermarkAssetLimitSetting(assetLimitBox.Text);
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

        private static bool IsProjectPath(string path) =>
            string.Equals(Path.GetExtension(path), ProjectExtension, StringComparison.OrdinalIgnoreCase);

        private bool ConfirmDiscardUnsavedChanges(string actionName)
        {
            if (!_hasUnsavedChanges) return true;

            string historyPath = CaptureHistoryVersion(actionName);
            string historyLine = historyPath == null
                ? "自动备份失败，建议先另存一份。"
                : $"已自动备份：{Path.GetFileName(historyPath)}";

            var result = AppDialog.Show(this,
                $"当前图片有未保存的修改。\n{historyLine}\n\n要先另存一份再{actionName}吗？",
                "保存更改？",
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
            UpdateStatus("已修改，记得点击右上角“保存”。");
        }

        private void SetCurrentFileName(string name)
        {
            string displayName = ShortenFileName(name);
            TopFileNameText.Text = displayName;
            TopFileNameText.ToolTip = name;
            CurrentFileText.Text = displayName;
            CurrentFileText.ToolTip = name;
            UpdateBottomOverlayConstraints();
            UpdateTitleFileInfoText();
        }

        private void UpdateImageInfo(BitmapSource image, long? fileBytes)
        {
            if (image == null)
            {
                ImageInfoBadge.Visibility = Visibility.Collapsed;
                return;
            }

            string resolution = $"{image.PixelWidth}×{image.PixelHeight}";
            string aspectRatio = FormatAspectRatio(image.PixelWidth, image.PixelHeight);
            ImageInfoText.Text = fileBytes.HasValue
                ? $"{resolution} | {aspectRatio} | {FormatFileSize(fileBytes.Value)}"
                : $"{resolution} | {aspectRatio}";
            ImageInfoText.ToolTip = fileBytes.HasValue
                ? $"图片分辨率：{resolution}\n图片比例：{aspectRatio}\n文件体积：{FormatFileSize(fileBytes.Value)}"
                : $"图片分辨率：{resolution}\n图片比例：{aspectRatio}";
            ImageInfoBadge.Visibility = Visibility.Visible;
            UpdateBottomOverlayConstraints();
            UpdateTitleFileInfoText();
        }

        private void UpdateTitleFileInfoText()
        {
            if (TitleFileInfoText == null) return;
            if (Canvas1.Image == null)
            {
                TitleFileInfoText.Text = "见微 PicMark";
                TitleFileInfoText.ToolTip = "见微 PicMark";
                Title = "见微 PicMark";
                return;
            }

            string name = TopFileNameText?.ToolTip as string;
            if (string.IsNullOrWhiteSpace(name))
                name = "当前图片";

            string sizeText = $"{Canvas1.Image.PixelWidth}×{Canvas1.Image.PixelHeight}像素";
            long? fileBytes = GetCurrentImageFileSize();
            if (fileBytes.HasValue)
                sizeText += $", {FormatFileSize(fileBytes.Value)}";

            string positionText = _viewerFiles.Count > 1 && _viewerIndex >= 0
                ? $" - 第{_viewerIndex + 1}/{_viewerFiles.Count}张"
                : string.Empty;
            string zoomText = $" {Math.Round(Canvas1.Scale * 100)}%";
            string title = $"{name} ({sizeText}) - 见微 PicMark{positionText}{zoomText}";

            TitleFileInfoText.Text = title;
            TitleFileInfoText.ToolTip = title;
            Title = title;
        }

        private void UpdateBottomOverlayConstraints()
        {
            if (ViewerBottomOverlay == null || BottomZoomBar == null || ImageInfoBadge == null || CurrentFileBadge == null || ImageInfoText == null)
                return;

            double overlayWidth = ViewerBottomOverlay.ActualWidth;
            double zoomWidth = BottomZoomBar.ActualWidth;
            if (overlayWidth <= 0 || zoomWidth <= 0) return;

            const double centerOuterMargin = 24;
            double sideWidth = Math.Max(0, (overlayWidth - zoomWidth - centerOuterMargin) / 2.0);
            double rightAvailable = Math.Max(0, sideWidth - 12);

            if (RightBottomBadges != null)
                RightBottomBadges.MaxWidth = rightAvailable;

            if (Canvas1.Image == null)
            {
                CurrentFileBadge.Visibility = Visibility.Collapsed;
                ImageInfoBadge.Visibility = Visibility.Collapsed;
                return;
            }

            double infoBadgeWidth = Math.Min(360, Math.Max(110, sideWidth));
            ImageInfoBadge.Visibility = Visibility.Visible;
            ImageInfoBadge.MaxWidth = infoBadgeWidth;
            ImageInfoText.MaxWidth = Math.Max(88, infoBadgeWidth - 22);

            if (rightAvailable < 150)
            {
                CurrentFileBadge.Visibility = Visibility.Collapsed;
                return;
            }

            bool showFileBadge = rightAvailable >= 365;
            double fileBadgeWidth = showFileBadge
                ? Math.Max(100, Math.Min(230, rightAvailable))
                : 0;

            CurrentFileBadge.Visibility = showFileBadge ? Visibility.Visible : Visibility.Collapsed;
            if (showFileBadge)
            {
                CurrentFileBadge.MaxWidth = fileBadgeWidth;
                CurrentFileText.MaxWidth = Math.Max(62, fileBadgeWidth - 22);
            }
        }

        private void AddRecentFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            _settings.RecentFiles.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
            _settings.RecentFiles.Insert(0, path);
            while (_settings.RecentFiles.Count > 8)
                _settings.RecentFiles.RemoveAt(_settings.RecentFiles.Count - 1);
            _settings.Save();
            UpdateRecentFilesUi();
        }

        private void UpdateRecentFilesUi(bool refreshThumbnails = true)
        {
            if (RecentFilesHost == null || EmptyState == null) return;

            int visibleLimit = GetRecentFilesVisibleLimit();
            int columns = visibleLimit == 8 ? 4 : 3;
            double panelWidth = GetRecentFilesPanelWidth(columns);
            double cardsWidth = panelWidth - 56;
            double cardWidth = Math.Floor(cardsWidth / columns) - 6;
            double imageHeight = GetRecentFilesImageHeight(cardWidth);
            double panelHeight = GetRecentFilesPanelHeight(imageHeight);
            bool layoutUnchanged = _recentFilesVisibleLimit == visibleLimit
                && Math.Abs(_recentFilesListWidth - cardsWidth) < 1;
            if (!refreshThumbnails && layoutUnchanged && RecentFilesHost.Children.Count > 0) return;

            _recentFilesVisibleLimit = visibleLimit;
            _recentFilesListWidth = cardsWidth;
            EmptyState.Width = panelWidth;
            EmptyState.Height = panelHeight;
            RecentFilesHost.Width = cardsWidth;
            RecentFilesHost.HorizontalAlignment = HorizontalAlignment.Center;
            RecentFilesHost.Children.Clear();

            // Keep one card permanently for opening an image.  The remaining
            // positions are recent files: 7 on normal windows, 5 on compact ones.
            int recentCapacity = Math.Max(0, visibleLimit - 1);
            var existing = _settings.RecentFiles.Where(File.Exists).Take(recentCapacity).ToList();

            RecentFilesHost.Children.Add(new TextBlock
            {
                Text = "最近打开",
                Foreground = new SolidColorBrush(Color.FromRgb(0x4F, 0x5D, 0x6C)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 7)
            });

            var cards = new System.Windows.Controls.Primitives.UniformGrid
            {
                Columns = columns,
                Width = cardsWidth,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            foreach (string path in existing)
            {
                string formatLabel = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
                if (formatLabel == "JPEG") formatLabel = "JPG";
                if (string.IsNullOrWhiteSpace(formatLabel)) formatLabel = "IMG";
                var thumbnail = new Image
                {
                    Width = cardWidth,
                    Height = imageHeight,
                    Stretch = Stretch.UniformToFill,
                    ClipToBounds = true,
                    SnapsToDevicePixels = true
                };
                BitmapSource preview = LoadRecentFileThumbnail(path, (int)Math.Ceiling(cardWidth * 2));
                if (preview != null)
                    thumbnail.Source = preview;

                var previewContent = new Grid { ClipToBounds = true };
                previewContent.Children.Add(thumbnail);
                previewContent.Children.Add(new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 7, 7),
                    Padding = new Thickness(7, 3, 7, 3),
                    Background = new SolidColorBrush(Color.FromArgb(232, 255, 239, 190)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(210, 237, 183, 76)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Child = new TextBlock
                    {
                        Text = formatLabel,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x57, 0x00)),
                        FontSize = 9.5,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                });

                var previewFrame = new Border
                {
                    Height = imageHeight + 2,
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xD5, 0xE0, 0xE8)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3, 3, 0, 0),
                    ClipToBounds = true,
                    Child = previewContent
                };

                var name = new TextBlock
                {
                    Text = Path.GetFileName(path),
                    Foreground = DialogPrimaryTextBrush(),
                    FontSize = 12.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Width = Math.Max(24, cardWidth - 16),
                    Margin = new Thickness(8, 0, 8, 0)
                };

                var content = new Grid { ClipToBounds = true };
                content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(imageHeight) });
                content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
                content.Children.Add(previewFrame);
                Grid.SetRow(name, 1);
                content.Children.Add(name);

                var button = new Button
                {
                    Content = content,
                    ToolTip = path,
                    Tag = path,
                    Width = cardWidth,
                    Height = imageHeight + 34,
                    Margin = new Thickness(0, 0, 6, 6),
                    Padding = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch,
                    Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xFA, 0xFB)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xB9, 0xCB, 0xD8)),
                    BorderThickness = new Thickness(1)
                };
                System.Windows.Automation.AutomationProperties.SetName(button, Path.GetFileName(path));
                button.Click += RecentFile_Click;
                cards.Children.Add(button);
            }

            var openCardContent = new Grid
            {
                Width = cardWidth,
                Height = imageHeight + 34,
                ClipToBounds = true
            };
            openCardContent.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x9C, 0xB2, 0xC3)),
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                RadiusX = 3,
                RadiusY = 3
            });
            var openCardLabel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            openCardLabel.Children.Add(new TextBlock
            {
                Text = "+",
                Foreground = new SolidColorBrush(Color.FromRgb(0x63, 0x70, 0x83)),
                FontSize = 28,
                FontWeight = FontWeights.Light,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            openCardLabel.Children.Add(new TextBlock
            {
                Text = "打开图片",
                Foreground = new SolidColorBrush(Color.FromRgb(0x63, 0x70, 0x83)),
                FontSize = 12,
                Margin = new Thickness(0, 1, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            openCardContent.Children.Add(openCardLabel);
            var openCard = new Button
            {
                Content = openCardContent,
                ToolTip = "打开图片",
                Width = cardWidth,
                Height = imageHeight + 34,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };
            System.Windows.Automation.AutomationProperties.SetName(openCard, "打开图片");
            openCard.Click += BtnOpen_Click;
            cards.Children.Add(openCard);

            RecentFilesHost.Children.Add(cards);
        }

        private int GetRecentFilesVisibleLimit()
        {
            double stageWidth = ImageStage?.ActualWidth > 0 ? ImageStage.ActualWidth : ActualWidth;
            double stageHeight = ImageStage?.ActualHeight > 0 ? ImageStage.ActualHeight : ActualHeight;
            // Smaller windows keep two rows but reduce from four columns to three.
            return stageWidth < 880 || stageHeight < 520 ? 6 : 8;
        }

        private double GetRecentFilesPanelWidth(int columns)
        {
            double stageWidth = ImageStage?.ActualWidth > 0 ? ImageStage.ActualWidth : ActualWidth;
            if (stageWidth <= 0) stageWidth = 760;
            double preferredWidth = columns == 4 ? 600 : 470;
            return Math.Max(340, Math.Min(preferredWidth, stageWidth - 64));
        }

        private double GetRecentFilesImageHeight(double cardWidth)
        {
            double stageHeight = ImageStage?.ActualHeight > 0 ? ImageStage.ActualHeight : ActualHeight - 100;
            // Leave room for the heading, description, and two title rows.  On a
            // shorter window thumbnails shrink before the second row can overflow.
            double maximumImageHeight = Math.Max(42, (stageHeight - 194) / 2 - 40);
            return Math.Floor(Math.Max(42, Math.Min(Math.Min(cardWidth * 0.66, 88), maximumImageHeight)));
        }

        private double GetRecentFilesPanelHeight(double imageHeight)
        {
            double stageHeight = ImageStage?.ActualHeight > 0 ? ImageStage.ActualHeight : ActualHeight - 100;
            double contentHeight = 190 + 2 * (imageHeight + 40);
            return Math.Min(Math.Max(292, stageHeight - 24), contentHeight);
        }

        private BitmapSource LoadRecentFileThumbnail(string path, int decodePixelWidth)
        {
            string cacheKey = path + "|" + decodePixelWidth.ToString(CultureInfo.InvariantCulture);
            if (_recentFileThumbnailCache.TryGetValue(cacheKey, out BitmapSource cached))
                return cached;

            try
            {
                BitmapSource thumbnail;
                if (IsProjectPath(path))
                    thumbnail = ProjectStore.LoadPreview(path, decodePixelWidth);
                else if (string.Equals(Path.GetExtension(path), ".webp", StringComparison.OrdinalIgnoreCase))
                    thumbnail = CreateThumbnail(WebpDecoder.Load(path), decodePixelWidth);
                else
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = decodePixelWidth;
                    image.UriSource = new Uri(path, UriKind.Absolute);
                    image.EndInit();
                    image.Freeze();
                    thumbnail = image;
                }

                if (thumbnail != null)
                    _recentFileThumbnailCache[cacheKey] = thumbnail;
                return thumbnail;
            }
            catch
            {
                return null;
            }
        }

        private static BitmapSource CreateThumbnail(BitmapSource source, int maximumSize)
        {
            if (source == null || source.PixelWidth <= maximumSize && source.PixelHeight <= maximumSize)
                return source;

            double scale = Math.Min((double)maximumSize / source.PixelWidth, (double)maximumSize / source.PixelHeight);
            var thumbnail = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            thumbnail.Freeze();
            return thumbnail;
        }
        private void RecentFile_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is string path)) return;
            if (!File.Exists(path))
            {
                _settings.RecentFiles.Remove(path);
                _settings.Save();
                UpdateRecentFilesUi();
                return;
            }
            if (!ConfirmDiscardUnsavedChanges("打开最近文件")) return;
            ClearBatch();
            if (IsProjectPath(path))
                LoadProject(path);
            else
                LoadImage(path);
        }

        private static long? TryGetFileSize(string path)
        {
            try
            {
                return new FileInfo(path).Length;
            }
            catch
            {
                return null;
            }
        }

        private long? GetCurrentImageFileSize()
        {
            string path = GetCurrentDiskPath();
            return string.IsNullOrWhiteSpace(path) ? null : TryGetFileSize(path);
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return $"{bytes / 1024.0 / 1024.0 / 1024.0:0.##} GB";
            if (bytes >= 1024L * 1024)
                return $"{bytes / 1024.0 / 1024.0:0.##} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:0.##} KB";
            return $"{bytes} B";
        }

        private static string FormatAspectRatio(int width, int height)
        {
            if (width <= 0 || height <= 0) return "-";
            double ratio = width / (double)height;
            var commonRatios = new[]
            {
                Tuple.Create(1.0, "1:1"),
                Tuple.Create(4.0 / 3.0, "4:3"),
                Tuple.Create(3.0 / 4.0, "3:4"),
                Tuple.Create(16.0 / 9.0, "16:9"),
                Tuple.Create(9.0 / 16.0, "9:16"),
                Tuple.Create(3.0 / 2.0, "3:2"),
                Tuple.Create(2.0 / 3.0, "2:3"),
                Tuple.Create(21.0 / 9.0, "21:9"),
                Tuple.Create(9.0 / 21.0, "9:21")
            };

            foreach (var item in commonRatios)
            {
                if (Math.Abs(ratio - item.Item1) <= 0.015)
                    return item.Item2;
            }

            double normalizedWidth = ratio * 9.0;
            double rounded = Math.Round(normalizedWidth, 1, MidpointRounding.AwayFromZero);
            if (Math.Abs(rounded - Math.Round(rounded)) < 0.05)
                return $"{(int)Math.Round(rounded)}:9";
            return $"{rounded.ToString("0.#", CultureInfo.InvariantCulture)}:9";
        }

        private static int GreatestCommonDivisor(int a, int b)
        {
            a = Math.Abs(a);
            b = Math.Abs(b);
            while (b != 0)
            {
                int temp = a % b;
                a = b;
                b = temp;
            }
            return Math.Max(a, 1);
        }

        // 底部状态提示已移除，避免与图片信息争抢空间。
        // 仍保留调用入口，便于以后在需要时改接短暂提示，而不干扰当前浏览界面。
        private void UpdateStatus(string text) { }

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
