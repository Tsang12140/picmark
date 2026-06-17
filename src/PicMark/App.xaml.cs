using System.Windows;

namespace PicMark
{
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            var window = new MainWindow();
            window.Show();
            window.OpenInitialFiles(e.Args);
        }
    }
}
