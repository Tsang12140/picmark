using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace PicMark
{
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            var window = new MainWindow();
            var args = Environment.GetCommandLineArgs()
                .Skip(1)
                .Concat(e.Args ?? new string[0])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            window.OpenInitialFiles(args);
            window.Show();
        }
    }
}
