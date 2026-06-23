using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PicMark
{
    internal enum WatermarkOverwriteChoice
    {
        Cancel,
        SaveAs,
        Overwrite
    }

    internal sealed class WatermarkOverwriteDialog : Window
    {
        public WatermarkOverwriteChoice Choice { get; private set; } = WatermarkOverwriteChoice.Cancel;

        private WatermarkOverwriteDialog()
        {
            Width = 560;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            FontFamily = UiFonts.Family;
            Content = BuildContent();
        }

        public static WatermarkOverwriteChoice Show(Window owner)
        {
            var dialog = new WatermarkOverwriteDialog { Owner = owner };
            dialog.ShowDialog();
            return dialog.Choice;
        }

        private UIElement BuildContent()
        {
            var root = new Grid { Margin = new Thickness(24) };
            var panel = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(58, 58, 58)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(82, 82, 82)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(24, 20, 24, 24),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 24,
                    ShadowDepth = 5,
                    Opacity = 0.5
                }
            };

            var layout = new StackPanel();
            var titleBar = new Grid { Margin = new Thickness(0, 0, 0, 18), Cursor = Cursors.SizeAll };
            titleBar.MouseLeftButtonDown += (s, e) => DragMove();
            titleBar.Children.Add(new TextBlock
            {
                Text = "水印覆盖原图后无法恢复",
                Foreground = Brushes.White,
                FontSize = 17,
                FontWeight = FontWeights.Bold
            });
            var close = MakeButton("×", false);
            close.Width = 30;
            close.MinWidth = 30;
            close.Height = 28;
            close.Margin = new Thickness(0);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Click += (s, e) => CloseWith(WatermarkOverwriteChoice.Cancel);
            titleBar.Children.Add(close);
            layout.Children.Add(titleBar);

            layout.Children.Add(new TextBlock
            {
                Text = "当前图片已添加水印。覆盖保存会把水印永久写入原图，被遮挡的内容无法通过 PicMark 还原。\n\n建议另存为新图片，保留一份无水印原图。",
                Foreground = new SolidColorBrush(Color.FromRgb(238, 239, 242)),
                FontSize = 14,
                LineHeight = 23,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 26)
            });

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var saveAs = MakeButton("另存为（推荐）", true);
            saveAs.Click += (s, e) => CloseWith(WatermarkOverwriteChoice.SaveAs);
            var overwrite = MakeButton("仍要覆盖", false);
            overwrite.Click += (s, e) => CloseWith(WatermarkOverwriteChoice.Overwrite);
            actions.Children.Add(saveAs);
            actions.Children.Add(overwrite);
            layout.Children.Add(actions);

            panel.Child = layout;
            root.Children.Add(panel);
            return root;
        }

        private static Button MakeButton(string text, bool primary)
        {
            return new Button
            {
                Content = text,
                MinWidth = primary ? 130 : 92,
                Height = 36,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(16, 0, 16, 0),
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(primary
                    ? Color.FromRgb(82, 101, 255)
                    : Color.FromRgb(78, 78, 78)),
                BorderBrush = new SolidColorBrush(primary
                    ? Color.FromRgb(105, 121, 255)
                    : Color.FromRgb(100, 100, 100))
            };
        }

        private void CloseWith(WatermarkOverwriteChoice choice)
        {
            Choice = choice;
            DialogResult = true;
        }
    }
}
