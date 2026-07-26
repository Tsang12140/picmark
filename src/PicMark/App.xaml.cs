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
            window.Show();
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                var args = Environment.GetCommandLineArgs()
                    .Skip(1)
                    .Concat(e.Args ?? new string[0])
                    .ToArray();

                int batchCropIndex = Array.FindIndex(args, a =>
                    string.Equals(a, "/batchcrop", StringComparison.OrdinalIgnoreCase));

                if (batchCropIndex >= 0 && batchCropIndex + 1 < args.Length)
                {
                    window.OpenBatchCropForPath(args[batchCropIndex + 1]);
                }
                else
                {
                    window.OpenInitialFiles(args.Distinct(StringComparer.OrdinalIgnoreCase));
                }
            }), DispatcherPriority.ApplicationIdle);
        }
    }
}
