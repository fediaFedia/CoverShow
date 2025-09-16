using System.Windows;
using System;
using System.IO;
using System.Windows.Forms;
namespace Wpf.CoverShow
{
    /// <summary>
    /// Logique d'interaction pour App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            string path = null;
            if (e.Args != null && e.Args.Length > 0)
            {
                path = e.Args[0];
            }
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "Select a folder containing images";
                    dialog.ShowNewFolderButton = false;
                    var result = dialog.ShowDialog();
                    if (result == System.Windows.Forms.DialogResult.OK)
                        path = dialog.SelectedPath;
                }
            }
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                Shutdown();
                return;
            }
            var window = new TestWindow(path);
            window.Show();
        }
    }
}
