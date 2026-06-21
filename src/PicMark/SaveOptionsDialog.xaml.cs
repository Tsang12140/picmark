using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WinForms = System.Windows.Forms;

namespace PicMark
{
    public partial class SaveOptionsDialog : Window
    {
        private readonly BitmapSource _source;
        private readonly int _sourceWidth;
        private readonly int _sourceHeight;
        private bool _updating;

        public string TargetPath { get; private set; }
        public string TargetExtension { get; private set; }
        public int OutputWidth { get; private set; }
        public int OutputHeight { get; private set; }
        public int Quality { get; private set; }
        public long? TargetBytes { get; private set; }

        public SaveOptionsDialog(BitmapSource source, string currentPath, string currentExtension)
        {
            InitializeComponent();
            _source = source;
            _sourceWidth = source.PixelWidth;
            _sourceHeight = source.PixelHeight;
            PreviewImage.Source = source;

            string baseDir = !string.IsNullOrWhiteSpace(currentPath)
                ? Path.GetDirectoryName(currentPath)
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string baseName = !string.IsNullOrWhiteSpace(currentPath)
                ? Path.GetFileNameWithoutExtension(currentPath) + "_标注"
                : "粘贴图片_标注";
            string ext = NormalizeExtension(currentExtension);

            NameBox.Text = baseName;
            PathBox.Text = baseDir;
            SelectExtension(ext);
            SetSize(_sourceWidth, _sourceHeight);
            UpdatePreviewInfo();
        }

        private void SetSize(int width, int height)
        {
            _updating = true;
            WidthBox.Text = width.ToString(CultureInfo.InvariantCulture);
            HeightBox.Text = height.ToString(CultureInfo.InvariantCulture);
            PercentBox.Text = Math.Round(width * 100.0 / _sourceWidth).ToString(CultureInfo.InvariantCulture);
            _updating = false;
        }

        private void SelectExtension(string ext)
        {
            foreach (ComboBoxItem item in ExtCombo.Items)
            {
                if (string.Equals((string)item.Content, ext, StringComparison.OrdinalIgnoreCase))
                {
                    ExtCombo.SelectedItem = item;
                    return;
                }
            }
            ExtCombo.SelectedIndex = 0;
        }

        private static string NormalizeExtension(string ext)
        {
            ext = (ext ?? ".png").ToLowerInvariant();
            if (ext == ".jpeg") return ".jpg";
            return ext == ".jpg" || ext == ".bmp" || ext == ".webp" ? ext : ".png";
        }

        private void SizeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating) return;
            if (!int.TryParse(WidthBox.Text, out int width) || width <= 0) return;
            int height = Math.Max(1, (int)Math.Round(width * _sourceHeight / (double)_sourceWidth));
            _updating = true;
            HeightBox.Text = height.ToString(CultureInfo.InvariantCulture);
            PercentBox.Text = Math.Round(width * 100.0 / _sourceWidth).ToString(CultureInfo.InvariantCulture);
            _updating = false;
            UpdatePreviewInfo();
        }

        private void PercentBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating) return;
            if (!double.TryParse(PercentBox.Text, out double percent) || percent <= 0) return;
            int width = Math.Max(1, (int)Math.Round(_sourceWidth * percent / 100.0));
            int height = Math.Max(1, (int)Math.Round(_sourceHeight * percent / 100.0));
            _updating = true;
            WidthBox.Text = width.ToString(CultureInfo.InvariantCulture);
            HeightBox.Text = height.ToString(CultureInfo.InvariantCulture);
            _updating = false;
            UpdatePreviewInfo();
        }

        private void CompressionRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (TargetSizeBox == null) return;
            bool enabled = CompressRadio.IsChecked == true;
            TargetSizeBox.IsEnabled = enabled;
            TargetUnitCombo.IsEnabled = enabled;
            UpdatePreviewInfo();
        }

        private void AnyTextChanged(object sender, TextChangedEventArgs e) => UpdatePreviewInfo();
        private void AnyOptionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreviewInfo();

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                dlg.SelectedPath = Directory.Exists(PathBox.Text) ? PathBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                    PathBox.Text = dlg.SelectedPath;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(WidthBox.Text, out int width) || !int.TryParse(HeightBox.Text, out int height) || width <= 0 || height <= 0)
            {
                AppDialog.Show(this, "尺寸不正确。", "提示");
                return;
            }
            // Win7 环境内存受限，限制最大输出尺寸
            const int maxDimension = 16384;
            if (width > maxDimension || height > maxDimension)
            {
                AppDialog.Show(this, $"输出尺寸不能超过 {maxDimension} 像素。", "提示");
                return;
            }
            string dir = PathBox.Text.Trim();
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); }
                catch (Exception ex)
                {
                    AppDialog.Show(this, $"无法创建目录：{ex.Message}", "错误");
                    return;
                }
            }
            string name = string.IsNullOrWhiteSpace(NameBox.Text) ? "未命名" : NameBox.Text.Trim();
            string ext = GetSelectedExtension();
            TargetPath = Path.Combine(dir, name + ext);
            TargetExtension = ext;
            OutputWidth = width;
            OutputHeight = height;
            Quality = GetSelectedQuality();
            TargetBytes = GetTargetBytes();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
                DragMove();
        }

        private string GetSelectedExtension() =>
            (ExtCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? ".png";

        private int GetSelectedQuality()
        {
            var item = QualityCombo.SelectedItem as ComboBoxItem;
            return int.TryParse((string)item?.Tag, out int q) ? q : 95;
        }

        private long? GetTargetBytes()
        {
            if (CompressRadio == null || TargetSizeBox == null || TargetUnitCombo == null) return null;
            if (CompressRadio.IsChecked != true) return null;
            if (!double.TryParse(TargetSizeBox.Text, out double value) || value <= 0) return null;
            var unit = TargetUnitCombo.SelectedItem as ComboBoxItem;
            bool mb = unit?.Content?.ToString() == "MB";
            return (long)(value * (mb ? 1024 * 1024 : 1024));
        }

        private void UpdatePreviewInfo()
        {
            if (PreviewInfoText == null) return;
            string ext = ExtCombo?.SelectedItem is ComboBoxItem ? GetSelectedExtension() : ".png";
            string sizeText = $"{WidthBox?.Text ?? _sourceWidth.ToString()} × {HeightBox?.Text ?? _sourceHeight.ToString()}";
            string qualityText = ext == ".jpg" || ext == ".webp" ? $"，质量 {GetSelectedQuality()}%" : string.Empty;
            string targetText = GetTargetBytes() is long bytes ? $"，目标约 {bytes / 1024.0:0.#} KB" : string.Empty;
            PreviewInfoText.Text = $"{sizeText}{qualityText}{targetText}";
            if (EstimateText != null)
            {
                bool resized = int.TryParse(WidthBox?.Text, out int width) &&
                    int.TryParse(HeightBox?.Text, out int height) &&
                    (width != _sourceWidth || height != _sourceHeight);
                string resizeNote = resized
                    ? "当前尺寸不是原图尺寸，会重新缩放整张图。"
                    : "当前保持原图分辨率。";
                EstimateText.Text = ext == ".jpg" || ext == ".webp"
                    ? $"JPG/WebP 适合小体积分享，但会重新编码，不能做到严格无损。{resizeNote}"
                    : $"PNG/BMP 是无损保存，适合保留原图画质和精确标注。{resizeNote}";
            }
        }
    }
}
