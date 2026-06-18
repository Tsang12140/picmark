using System.Windows;
using System.Windows.Input;

namespace PicMark
{
    public partial class TextInputDialog : Window
    {
        public string ResultText { get; private set; }
        public double ResultFontSize { get; private set; } = 36;
        public double InitialFontSize { get; set; } = 36;

        private string _editingExistingText;
        public string EditingExistingText
        {
            get => _editingExistingText;
            set
            {
                _editingExistingText = value;
                TxtInput.Text = value ?? string.Empty;
                Title = "编辑文字";
                TitleText.Text = "编辑文字";
            }
        }

        public TextInputDialog()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                SelectFontSize(InitialFontSize);
                TxtInput.Focus();
                TxtInput.SelectAll();
            };
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            ResultText = TxtInput.Text;
            ResultFontSize = GetSelectedFontSize();
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void TxtInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ResultText = TxtInput.Text;
                ResultFontSize = GetSelectedFontSize();
                DialogResult = true;
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
            }
        }

        private void FontSizeBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            double size = GetSelectedFontSize();
            ResultFontSize = size;
            TxtInput.FontSize = size;
        }

        private double GetSelectedFontSize()
        {
            if (FontSizeBox.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
                double.TryParse(item.Tag?.ToString(), out double size))
            {
                return size;
            }
            return InitialFontSize;
        }

        private void SelectFontSize(double fontSize)
        {
            double bestDistance = double.MaxValue;
            System.Windows.Controls.ComboBoxItem bestItem = null;
            foreach (System.Windows.Controls.ComboBoxItem item in FontSizeBox.Items)
            {
                if (!double.TryParse(item.Tag?.ToString(), out double size)) continue;
                double distance = System.Math.Abs(size - fontSize);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestItem = item;
                }
            }
            FontSizeBox.SelectedItem = bestItem ?? FontSizeBox.Items[0];
            ResultFontSize = GetSelectedFontSize();
            TxtInput.FontSize = ResultFontSize;
        }
    }
}
