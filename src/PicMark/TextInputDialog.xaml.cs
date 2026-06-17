using System.Windows;
using System.Windows.Input;

namespace PicMark
{
    public partial class TextInputDialog : Window
    {
        public string ResultText { get; private set; }

        private string _editingExistingText;
        public string EditingExistingText
        {
            get => _editingExistingText;
            set
            {
                _editingExistingText = value;
                TxtInput.Text = value ?? string.Empty;
                Title = "编辑文字";
            }
        }

        public TextInputDialog()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                TxtInput.Focus();
                TxtInput.SelectAll();
            };
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            ResultText = TxtInput.Text;
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
                DialogResult = true;
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
            }
        }
    }
}
