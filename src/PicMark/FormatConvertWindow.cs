using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace PicMark
{
    internal sealed class FormatConvertWindow : Window
    {
        private static readonly string[] TargetExtensions = { ".png", ".jpg", ".webp", ".bmp" };

        private readonly WrapPanel _targetPanel = new WrapPanel();
        private readonly Image _previewImage = new Image();
        private readonly TextBlock _previewName = new TextBlock();
        private readonly TextBlock _previewMeta = new TextBlock();
        private readonly TextBlock _sourceText = new TextBlock();
        private readonly TextBox _outputBox = new TextBox();
        private readonly CheckBox _overwriteCheck = new CheckBox();
        private readonly TextBlock _qualityValueText = new TextBlock();
        private readonly Slider _qualitySlider = new Slider();
        private readonly TextBlock _statusText = new TextBlock();
        private readonly Button _convertButton = new Button();
        private readonly StackPanel _resultActions = new StackPanel();
        private readonly Button _openOutputButton = new Button();
        private readonly Button _openFolderButton = new Button();

        private string _sourcePath;
        private BitmapSource _sourceBitmap;
        private string _sourceName;
        private string _sourceExtension = ".png";
        private string _targetExtension = ".jpg";
        private string _lastOutputFile;
        private bool _running;

        public FormatConvertWindow(System.Collections.Generic.IEnumerable<string> initialFiles, string initialDirectory)
            : this(initialFiles, initialDirectory, null, null, null)
        {
        }

        public FormatConvertWindow(
            System.Collections.Generic.IEnumerable<string> initialFiles,
            string initialDirectory,
            BitmapSource initialBitmap,
            string initialName,
            string initialExtension)
        {
            Title = "图片格式转换";
            Width = 900;
            Height = 610;
            MinWidth = 820;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ResizeMode = ResizeMode.NoResize;
            Background = Brushes.Transparent;
            FontFamily = UiFonts.Family;
            AllowDrop = true;
            DragOver += Window_DragOver;
            Drop += Window_Drop;

            _sourcePath = initialFiles?.FirstOrDefault(path => File.Exists(path) && ImageConversionService.IsSupportedInput(path));
            if (_sourcePath == null && initialBitmap != null)
            {
                if (initialBitmap.CanFreeze && !initialBitmap.IsFrozen)
                    initialBitmap.Freeze();
                _sourceBitmap = initialBitmap;
                _sourceName = string.IsNullOrWhiteSpace(initialName) ? "PicMark 图片" : initialName;
                _sourceExtension = ImageConversionService.NormalizeExtension(initialExtension);
            }
            _outputBox.Text = !string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory)
                ? initialDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            Content = BuildContent();
            RefreshAll();
        }

        private UIElement BuildContent()
        {
            var shell = new Border
            {
                Background = BrushFromRgb(0x30, 0x30, 0x30),
                BorderBrush = BrushFromRgb(0x4B, 0x4B, 0x4B),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(22)
            };

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.Child = layout;

            var titleBar = BuildTitleBar();
            Grid.SetRow(titleBar, 0);
            layout.Children.Add(titleBar);

            var targetArea = new StackPanel { Margin = new Thickness(0, 14, 0, 16) };
            _sourceText.Foreground = BrushFromRgb(0xC8, 0xCE, 0xD8);
            _sourceText.FontSize = 13;
            _sourceText.Margin = new Thickness(2, 0, 0, 8);
            targetArea.Children.Add(_sourceText);
            targetArea.Children.Add(_targetPanel);
            Grid.SetRow(targetArea, 1);
            layout.Children.Add(targetArea);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.18, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(body, 2);
            layout.Children.Add(body);

            var previewPanel = BuildPreviewPanel();
            Grid.SetColumn(previewPanel, 0);
            body.Children.Add(previewPanel);

            var options = BuildOptionPanel();
            Grid.SetColumn(options, 2);
            body.Children.Add(options);

            var footer = BuildFooter();
            Grid.SetRow(footer, 3);
            layout.Children.Add(footer);
            return shell;
        }

        private UIElement BuildTitleBar()
        {
            var titleBar = new Grid { Cursor = Cursors.SizeAll };
            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 1)
                {
                    try { DragMove(); }
                    catch (InvalidOperationException) { }
                }
            };
            titleBar.ColumnDefinitions.Add(new ColumnDefinition());
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel();
            titleStack.Children.Add(new TextBlock
            {
                Text = "图片格式转换",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });
            titleBar.Children.Add(titleStack);

            var close = MakeIconButton("×");
            close.Click += (s, e) => Close();
            Grid.SetColumn(close, 1);
            titleBar.Children.Add(close);
            return titleBar;
        }

        private UIElement BuildPreviewPanel()
        {
            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var addButton = MakeButton("选择图片", true);
            addButton.Click += AddButton_Click;
            var clearButton = MakeButton("清空", false);
            clearButton.Margin = new Thickness(8, 0, 0, 0);
            clearButton.Click += (s, e) =>
            {
                _sourcePath = null;
                _sourceBitmap = null;
                _sourceName = null;
                _sourceExtension = ".png";
                _lastOutputFile = null;
                RefreshAll();
            };
            actions.Children.Add(addButton);
            actions.Children.Add(clearButton);
            panel.Children.Add(actions);

            var previewFrame = new Border
            {
                Background = BrushFromRgb(0x26, 0x26, 0x26),
                BorderBrush = BrushFromRgb(0x4A, 0x4A, 0x4A),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10)
            };
            _previewImage.Stretch = Stretch.Uniform;
            _previewImage.SnapsToDevicePixels = true;
            previewFrame.Child = _previewImage;
            Grid.SetRow(previewFrame, 1);
            panel.Children.Add(previewFrame);

            var meta = new StackPanel { Margin = new Thickness(2, 9, 0, 0) };
            _previewName.Foreground = Brushes.White;
            _previewName.FontSize = 13;
            _previewName.TextTrimming = TextTrimming.CharacterEllipsis;
            _previewMeta.Foreground = BrushFromRgb(0xA9, 0xB0, 0xBD);
            _previewMeta.FontSize = 12;
            _previewMeta.Margin = new Thickness(0, 3, 0, 0);
            meta.Children.Add(_previewName);
            meta.Children.Add(_previewMeta);
            Grid.SetRow(meta, 2);
            panel.Children.Add(meta);

            return panel;
        }

        private UIElement BuildOptionPanel()
        {
            var options = new StackPanel();
            AddLabel(options, "图片质量");

            var qualityHeader = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            qualityHeader.ColumnDefinitions.Add(new ColumnDefinition());
            qualityHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            qualityHeader.Children.Add(new TextBlock
            {
                Text = "JPG / WebP 使用，PNG 和 BMP 会忽略",
                Foreground = BrushFromRgb(0xA9, 0xB0, 0xBD),
                FontSize = 12
            });
            _qualityValueText.Foreground = Brushes.White;
            _qualityValueText.FontWeight = FontWeights.SemiBold;
            Grid.SetColumn(_qualityValueText, 1);
            qualityHeader.Children.Add(_qualityValueText);
            options.Children.Add(qualityHeader);

            _qualitySlider.Minimum = 40;
            _qualitySlider.Maximum = 100;
            _qualitySlider.Value = 85;
            _qualitySlider.TickFrequency = 5;
            _qualitySlider.IsSnapToTickEnabled = true;
            _qualitySlider.Margin = new Thickness(0, 0, 0, 16);
            _qualitySlider.ValueChanged += (s, e) => UpdateQualityText();
            options.Children.Add(_qualitySlider);

            _overwriteCheck.Content = "覆盖同名文件";
            _overwriteCheck.Foreground = Brushes.White;
            _overwriteCheck.Margin = new Thickness(0, 0, 0, 16);
            options.Children.Add(_overwriteCheck);

            AddLabel(options, "输出目录");
            var pathGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition());
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StyleTextBox(_outputBox);
            pathGrid.Children.Add(_outputBox);
            var browse = MakeButton("浏览", false);
            browse.Margin = new Thickness(8, 0, 0, 0);
            browse.Click += Browse_Click;
            Grid.SetColumn(browse, 1);
            pathGrid.Children.Add(browse);
            options.Children.Add(pathGrid);

            _resultActions.Orientation = Orientation.Horizontal;
            _resultActions.Visibility = Visibility.Collapsed;
            CopyButtonLook(_openOutputButton, true);
            _openOutputButton.Content = "打开成品";
            _openOutputButton.Click += (s, e) => OpenOutputFile();
            CopyButtonLook(_openFolderButton, false);
            _openFolderButton.Content = "打开成品文件夹";
            _openFolderButton.Margin = new Thickness(8, 0, 0, 0);
            _openFolderButton.Click += (s, e) => OpenOutputFolder();
            _resultActions.Children.Add(_openOutputButton);
            _resultActions.Children.Add(_openFolderButton);
            options.Children.Add(_resultActions);

            return options;
        }

        private UIElement BuildFooter()
        {
            var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition());
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText.Foreground = BrushFromRgb(0xC8, 0xCE, 0xD8);
            _statusText.VerticalAlignment = VerticalAlignment.Center;
            _statusText.TextWrapping = TextWrapping.Wrap;
            footer.Children.Add(_statusText);

            _convertButton.Content = "开始转换";
            CopyButtonLook(_convertButton, true);
            _convertButton.MinWidth = 110;
            _convertButton.Click += ConvertButton_Click;
            Grid.SetColumn(_convertButton, 1);
            footer.Children.Add(_convertButton);
            return footer;
        }

        private void RefreshAll()
        {
            RebuildTargets();
            UpdatePreview();
            UpdateQualityText();
            UpdateStatus();
        }

        private void RebuildTargets()
        {
            _targetPanel.Children.Clear();
            string sourceExt = GetSourceExtension();
            var targets = TargetExtensions
                .Where(ext => sourceExt == null || !string.Equals(ext, sourceExt, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!targets.Contains(_targetExtension, StringComparer.OrdinalIgnoreCase))
                _targetExtension = targets.FirstOrDefault() ?? ".png";

            _sourceText.Text = sourceExt == null
                ? string.Empty
                : $"当前格式：{FormatName(sourceExt)}，可转换为";

            foreach (string target in targets)
                _targetPanel.Children.Add(MakeTargetButton(sourceExt, target));
        }

        private Button MakeTargetButton(string sourceExt, string targetExt)
        {
            string text = sourceExt == null
                ? $"转为 {FormatName(targetExt)}"
                : $"{FormatName(sourceExt)} → {FormatName(targetExt)}";
            bool selected = string.Equals(targetExt, _targetExtension, StringComparison.OrdinalIgnoreCase);
            var button = MakeButton(text, selected);
            button.MinWidth = 116;
            button.Margin = new Thickness(0, 0, 8, 8);
            button.Tag = targetExt;
            button.Click += (s, e) =>
            {
                _targetExtension = (string)((Button)s).Tag;
                RebuildTargets();
                UpdateStatus();
            };
            return button;
        }

        private void UpdatePreview()
        {
            _previewImage.Source = null;
            if (!HasSource())
            {
                _previewName.Text = string.Empty;
                _previewMeta.Text = string.Empty;
                return;
            }

            try
            {
                BitmapSource bitmap = _sourceBitmap ?? ImageConversionService.LoadBitmap(_sourcePath);
                _previewImage.Source = bitmap;
                _previewName.Text = GetSourceDisplayName();
                _previewMeta.Text = GetSourceMeta(bitmap);
            }
            catch (Exception ex)
            {
                _previewName.Text = GetSourceDisplayName();
                _previewMeta.Text = ex.Message;
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = ImageConversionService.BuildOpenFilter(),
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;
            SetSource(dialog.FileName);
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new WinForms.FolderBrowserDialog())
            {
                dialog.SelectedPath = Directory.Exists(_outputBox.Text)
                    ? _outputBox.Text
                    : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (dialog.ShowDialog() == WinForms.DialogResult.OK)
                    _outputBox.Text = dialog.SelectedPath;
            }
        }

        private async void ConvertButton_Click(object sender, RoutedEventArgs e)
        {
            if (_running) return;
            if (!HasSource())
            {
                AppDialog.Show(this, "请先选择要转换的图片。", "提示");
                return;
            }

            string output = _outputBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(output))
            {
                AppDialog.Show(this, "请选择输出目录。", "提示");
                return;
            }

            var options = new ImageConversionOptions
            {
                TargetExtension = _targetExtension,
                Quality = (int)Math.Round(_qualitySlider.Value),
                OverwriteExisting = _overwriteCheck.IsChecked == true
            };

            SetRunning(true);
            ImageConversionResult result = await Task.Run(() => ConvertCurrentSource(output, options));
            SetRunning(false);
            UpdateResult(result);
        }

        private void UpdateResult(ImageConversionResult result)
        {
            if (result.Success)
            {
                _lastOutputFile = result.TargetPath;
                _resultActions.Visibility = Visibility.Visible;
                string sizeText = result.SourceBytes > 0
                    ? $"{FormatBytes(result.SourceBytes)} → {FormatBytes(result.TargetBytes)}"
                    : FormatBytes(result.TargetBytes);
                _statusText.Text = $"转换完成：{Path.GetFileName(result.TargetPath)}，{sizeText}";
            }
            else
            {
                _lastOutputFile = null;
                _resultActions.Visibility = Visibility.Collapsed;
                _statusText.Text = $"转换失败：{result.Message}";
            }
        }

        private void SetSource(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            if (!ImageConversionService.IsSupportedInput(path)) return;
            _sourcePath = path;
            _sourceBitmap = null;
            _sourceName = null;
            _sourceExtension = ImageConversionService.NormalizeExtension(Path.GetExtension(path));
            _lastOutputFile = null;
            _resultActions.Visibility = Visibility.Collapsed;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                _outputBox.Text = dir;
            RefreshAll();
        }

        private bool HasSource()
        {
            return _sourceBitmap != null
                || (!string.IsNullOrWhiteSpace(_sourcePath) && File.Exists(_sourcePath));
        }

        private string GetSourceExtension()
        {
            if (!string.IsNullOrWhiteSpace(_sourcePath))
                return ImageConversionService.NormalizeExtension(Path.GetExtension(_sourcePath));
            return _sourceBitmap != null ? ImageConversionService.NormalizeExtension(_sourceExtension) : null;
        }

        private string GetSourceDisplayName()
        {
            if (!string.IsNullOrWhiteSpace(_sourcePath))
                return Path.GetFileName(_sourcePath);
            return string.IsNullOrWhiteSpace(_sourceName) ? "PicMark 图片" : _sourceName;
        }

        private string GetSourceMeta(BitmapSource bitmap)
        {
            string ext = GetSourceExtension();
            string basic = $"{bitmap.PixelWidth}×{bitmap.PixelHeight} | {FormatName(ext)}";
            if (!string.IsNullOrWhiteSpace(_sourcePath) && File.Exists(_sourcePath))
                return $"{basic} | {FormatBytes(new FileInfo(_sourcePath).Length)}";
            return $"{basic} | 当前画面";
        }

        private ImageConversionResult ConvertCurrentSource(string outputDirectory, ImageConversionOptions options)
        {
            if (!string.IsNullOrWhiteSpace(_sourcePath) && File.Exists(_sourcePath))
                return ImageConversionService.Convert(_sourcePath, outputDirectory, options);

            var result = new ImageConversionResult { SourcePath = GetSourceDisplayName() };
            try
            {
                if (_sourceBitmap == null)
                    throw new InvalidOperationException("没有可转换的图片。");

                Directory.CreateDirectory(outputDirectory);
                string targetExt = ImageConversionService.NormalizeExtension(options.TargetExtension);
                if (!ImageConversionService.IsSupportedOutput(targetExt))
                    throw new NotSupportedException("暂不支持这个输出格式。");

                string targetPath = BuildMemoryTargetPath(outputDirectory, GetSourceDisplayName(), targetExt, options.OverwriteExisting);
                byte[] bytes = ImageConversionService.EncodeBitmap(_sourceBitmap, targetExt, options.Quality);
                File.WriteAllBytes(targetPath, bytes);

                result.Success = true;
                result.TargetPath = targetPath;
                result.SourceBytes = 0;
                result.TargetBytes = bytes.Length;
                result.Message = "完成";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
            }

            return result;
        }

        private static string BuildMemoryTargetPath(string outputDirectory, string sourceName, string targetExt, bool overwrite)
        {
            string baseName = Path.GetFileNameWithoutExtension(sourceName);
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = "PicMark";
            foreach (char invalid in Path.GetInvalidFileNameChars())
                baseName = baseName.Replace(invalid, '_');

            string path = Path.Combine(outputDirectory, baseName + targetExt);
            if (overwrite || !File.Exists(path)) return path;

            int index = 2;
            while (true)
            {
                string candidate = Path.Combine(outputDirectory, $"{baseName}({index}){targetExt}");
                if (!File.Exists(candidate)) return candidate;
                index++;
            }
        }

        private void UpdateStatus()
        {
            _convertButton.IsEnabled = !_running && HasSource();
            if (_running) return;
            _statusText.Text = HasSource() ? $"准备输出为 {FormatName(_targetExtension)}" : string.Empty;
        }

        private void UpdateQualityText()
        {
            _qualityValueText.Text = $"{(int)Math.Round(_qualitySlider.Value)}";
        }

        private void SetRunning(bool running)
        {
            _running = running;
            _convertButton.IsEnabled = !running;
            _convertButton.Content = running ? "转换中..." : "开始转换";
            if (running)
            {
                _resultActions.Visibility = Visibility.Collapsed;
                _statusText.Text = "正在转换，请稍候...";
            }
        }

        private void OpenOutputFile()
        {
            if (string.IsNullOrWhiteSpace(_lastOutputFile) || !File.Exists(_lastOutputFile)) return;
            Process.Start(new ProcessStartInfo(_lastOutputFile) { UseShellExecute = true });
        }

        private void OpenOutputFolder()
        {
            if (string.IsNullOrWhiteSpace(_lastOutputFile)) return;
            try
            {
                if (File.Exists(_lastOutputFile))
                {
                    Process.Start("explorer.exe", $"/select,\"{_lastOutputFile}\"");
                    return;
                }

                string dir = Path.GetDirectoryName(_lastOutputFile);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, $"无法打开成品位置：{ex.Message}", "提示");
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string path = ((string[])e.Data.GetData(DataFormats.FileDrop))
                .FirstOrDefault(file => File.Exists(file) && ImageConversionService.IsSupportedInput(file));
            SetSource(path);
        }

        private static void AddLabel(Panel parent, string text)
        {
            parent.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 7)
            });
        }

        private static void StyleTextBox(TextBox textBox)
        {
            textBox.Height = 34;
            textBox.Padding = new Thickness(8, 5, 8, 5);
            textBox.Background = BrushFromRgb(0x2B, 0x2B, 0x2B);
            textBox.Foreground = Brushes.White;
            textBox.BorderBrush = BrushFromRgb(0x56, 0x56, 0x56);
            textBox.BorderThickness = new Thickness(1);
            textBox.VerticalContentAlignment = VerticalAlignment.Center;
        }

        private static Button MakeButton(string text, bool primary)
        {
            var button = new Button { Content = text };
            CopyButtonLook(button, primary);
            return button;
        }

        private static Button MakeIconButton(string text)
        {
            var button = new Button { Content = text };
            CopyButtonLook(button, false);
            button.Width = 34;
            button.Height = 34;
            button.MinWidth = 0;
            button.Padding = new Thickness(0);
            button.FontSize = 16;
            return button;
        }

        private static void CopyButtonLook(Button button, bool primary)
        {
            button.MinWidth = 78;
            button.Height = 34;
            button.Padding = new Thickness(13, 0, 13, 0);
            button.Cursor = Cursors.Hand;
            button.FontWeight = primary ? FontWeights.Bold : FontWeights.SemiBold;
            button.Foreground = Brushes.White;
            button.Background = primary ? BrushFromRgb(0x52, 0x65, 0xFF) : BrushFromRgb(0x43, 0x43, 0x43);
            button.BorderBrush = primary ? BrushFromRgb(0x52, 0x65, 0xFF) : BrushFromRgb(0x5A, 0x5A, 0x5A);
            button.BorderThickness = new Thickness(1);
            button.Template = CreateButtonTemplate();
        }

        private static ControlTemplate CreateButtonTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.AppendChild(presenter);

            return new ControlTemplate(typeof(Button)) { VisualTree = border };
        }

        private static string FormatName(string extension)
        {
            extension = ImageConversionService.NormalizeExtension(extension);
            if (extension == ".jpg") return "JPG";
            if (extension == ".png") return "PNG";
            if (extension == ".webp") return "WebP";
            if (extension == ".bmp") return "BMP";
            if (extension == ".gif") return "GIF";
            if (extension == ".tif" || extension == ".tiff") return "TIFF";
            if (extension == ".heic" || extension == ".heif") return "HEIC";
            return extension.TrimStart('.').ToUpperInvariant();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024) return $"{bytes / 1024.0 / 1024.0:0.##} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:0.##} KB";
            return $"{bytes} B";
        }

        private static SolidColorBrush BrushFromRgb(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));
    }
}
