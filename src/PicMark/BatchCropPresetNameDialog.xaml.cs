using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PicMark
{
    public partial class BatchCropPresetNameDialog : Window
    {
        private static readonly (string Label, double Aspect)[] Ratios =
        {
            ("1:1", 1.0),
            ("4:3", 4.0 / 3),
            ("3:4", 3.0 / 4),
            ("16:9", 16.0 / 9),
            ("9:16", 9.0 / 16),
            ("16:10", 16.0 / 10),
            ("10:16", 10.0 / 16),
        };

        public string ResultName { get; private set; }

        public BatchCropPresetNameDialog(double? sampleAspect = null)
        {
            InitializeComponent();
            PlatformCombo.SelectedIndex = 0;
            RatioCombo.SelectedIndex = sampleAspect.HasValue ? ClosestRatioIndex(sampleAspect.Value) : 0;
            UpdatePreview();
        }

        private static int ClosestRatioIndex(double aspect)
        {
            int bestIndex = 0;
            double bestDiff = double.MaxValue;
            for (int i = 0; i < Ratios.Length; i++)
            {
                double diff = Math.Abs(Ratios[i].Aspect - aspect);
                if (diff < bestDiff) { bestDiff = diff; bestIndex = i; }
            }
            return bestIndex;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }

        private void Combo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool platformCustom = IsCustomSelected(PlatformCombo);
            bool ratioCustom = IsCustomSelected(RatioCombo);
            PlatformCustomBox.Visibility = platformCustom ? Visibility.Visible : Visibility.Collapsed;
            RatioCustomBox.Visibility = ratioCustom ? Visibility.Visible : Visibility.Collapsed;
            UpdatePreview();
        }

        private void CustomBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

        private static bool IsCustomSelected(ComboBox combo) =>
            (combo.SelectedItem as ComboBoxItem)?.Content as string == "自定义";

        private string CurrentPlatform() =>
            IsCustomSelected(PlatformCombo) ? PlatformCustomBox.Text.Trim() : (string)((ComboBoxItem)PlatformCombo.SelectedItem).Content;

        private string CurrentRatio() =>
            IsCustomSelected(RatioCombo) ? RatioCustomBox.Text.Trim() : (string)((ComboBoxItem)RatioCombo.SelectedItem).Content;

        private void UpdatePreview()
        {
            string name = CurrentPlatform() + CurrentRatio();
            PreviewText.Text = string.IsNullOrWhiteSpace(CurrentPlatform()) || string.IsNullOrWhiteSpace(CurrentRatio())
                ? "请填写平台和比例"
                : $"将保存为：{name}";
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            string platform = CurrentPlatform();
            string ratio = CurrentRatio();
            if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(ratio))
            {
                AppDialog.Show(this, "请填写完整的平台和比例。", "提示");
                return;
            }
            ResultName = platform + ratio;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
