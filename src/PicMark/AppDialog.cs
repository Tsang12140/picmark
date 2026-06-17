using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PicMark
{
    internal static class AppDialog
    {
        public static MessageBoxResult Show(Window owner, string message, string title, MessageBoxButton buttons = MessageBoxButton.OK)
        {
            var dialog = new AppDialogWindow(title, message, buttons)
            {
                Owner = owner
            };
            dialog.ShowDialog();
            return dialog.Result;
        }
    }

    internal sealed class AppDialogWindow : Window
    {
        private readonly MessageBoxButton _buttons;

        public MessageBoxResult Result { get; private set; }

        public AppDialogWindow(string title, string message, MessageBoxButton buttons)
        {
            _buttons = buttons;
            Result = DefaultResult(buttons);

            Width = 520;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Alibaba PuHuiTi 3.0, Alibaba PuHuiTi, Microsoft YaHei UI, Microsoft YaHei, Segoe UI");

            Content = BuildContent(title, message);
        }

        private UIElement BuildContent(string title, string message)
        {
            var root = new Grid { Margin = new Thickness(24) };
            var panel = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(58, 58, 58)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(22, 18, 22, 22),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 24,
                    ShadowDepth = 5,
                    Opacity = 0.5
                }
            };

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var titleBar = new Grid { Margin = new Thickness(0, 0, 0, 22), Cursor = Cursors.SizeAll };
            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 1) DragMove();
            };

            var titleText = new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            titleBar.Children.Add(titleText);

            var close = new Button
            {
                Content = "×",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 28,
                Height = 28,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(218, 222, 230)),
                BorderThickness = new Thickness(0),
                FontSize = 18,
                Cursor = Cursors.Hand,
                Template = CreateButtonTemplate(0)
            };
            close.Click += (s, e) => CloseWith(Result);
            titleBar.Children.Add(close);
            Grid.SetRow(titleBar, 0);
            layout.Children.Add(titleBar);

            var messageText = new TextBlock
            {
                Text = message,
                Foreground = new SolidColorBrush(Color.FromRgb(244, 244, 245)),
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
            AddButtons(actions);
            Grid.SetRow(actions, 2);
            layout.Children.Add(actions);

            panel.Child = layout;
            root.Children.Add(panel);
            return root;
        }

        private void AddButtons(Panel actions)
        {
            switch (_buttons)
            {
                case MessageBoxButton.OKCancel:
                    actions.Children.Add(MakeButton("确认", MessageBoxResult.OK, true));
                    actions.Children.Add(MakeButton("取消", MessageBoxResult.Cancel, false));
                    break;
                case MessageBoxButton.YesNo:
                    actions.Children.Add(MakeButton("确认", MessageBoxResult.Yes, true));
                    actions.Children.Add(MakeButton("取消", MessageBoxResult.No, false));
                    break;
                case MessageBoxButton.YesNoCancel:
                    actions.Children.Add(MakeButton("另存为", MessageBoxResult.Yes, true));
                    actions.Children.Add(MakeButton("不另存", MessageBoxResult.No, false));
                    actions.Children.Add(MakeButton("取消", MessageBoxResult.Cancel, false));
                    break;
                default:
                    actions.Children.Add(MakeButton("确认", MessageBoxResult.OK, true));
                    break;
            }
        }

        private Button MakeButton(string text, MessageBoxResult result, bool primary)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 72,
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(14, 0, 14, 0),
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(primary ? Color.FromRgb(82, 101, 255) : Color.FromRgb(70, 70, 70)),
                BorderBrush = new SolidColorBrush(primary ? Color.FromRgb(82, 101, 255) : Color.FromRgb(86, 86, 86)),
                Foreground = Brushes.White,
                Template = CreateButtonTemplate(5)
            };
            button.Click += (s, e) => CloseWith(result);
            return button;
        }

        private void CloseWith(MessageBoxResult result)
        {
            Result = result;
            DialogResult = true;
        }

        private static MessageBoxResult DefaultResult(MessageBoxButton buttons)
        {
            return buttons == MessageBoxButton.OK ? MessageBoxResult.OK : MessageBoxResult.Cancel;
        }

        private static ControlTemplate CreateButtonTemplate(double cornerRadius)
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(cornerRadius));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Button.PaddingProperty));
            border.AppendChild(content);
            template.VisualTree = border;
            return template;
        }
    }
}
