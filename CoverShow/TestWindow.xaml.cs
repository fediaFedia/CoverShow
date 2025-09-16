using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
namespace Wpf.CoverShow
{
    public partial class TestWindow : Window
    {
        private class FileInfoComparer : IComparer<FileInfo>
        {
            #region IComparer<FileInfo> Membres
            public int Compare(FileInfo x, FileInfo y)
            {
                return string.Compare(x.FullName, y.FullName);
            }
            #endregion
        }

        #region Handlers
        private void DoKeyDown(Key key)
        {
            switch (key)
            {
                case Key.Right:
                    flow.GoToNext();
                    break;
                case Key.Left:
                    flow.GoToPrevious();
                    break;
                case Key.PageUp:
                    flow.GoToNextPage();
                    break;
                case Key.PageDown:
                    flow.GoToPreviousPage();
                    break;
            }
            if (flow.Index != Convert.ToInt32(slider.Value))
                slider.Value = flow.Index;
        }
        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                ToggleSlideshow();
                e.Handled = true;
                return;
            }
            DoKeyDown(e.Key);
        }
        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                flow.ZoomBy(e.Delta);
            }
            else
            {
                if (e.Delta < 0)
                    DoKeyDown(Key.Right);
                else if (e.Delta > 0)
                    DoKeyDown(Key.Left);
            }
            e.Handled = true;
        }
        private void slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            flow.Index = Convert.ToInt32(slider.Value);
        }
        #endregion
        #region Private stuff
        public void Load(string imagePath)
        {
            var imageDir = new DirectoryInfo(imagePath);
            var images = new List<FileInfo>(imageDir.GetFiles("*.png")
                .Union(imageDir.GetFiles("*.jpg"))
                .Union(imageDir.GetFiles("*.jpeg"))
                .Union(imageDir.GetFiles("*.tiff"))
                .Union(imageDir.GetFiles("*.gif"))
                .Union(imageDir.GetFiles("*.mp3"))
                .Union(imageDir.GetFiles("*.mp4"))
            ).Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0).ToList();
            images.Sort(new FileInfoComparer());
            foreach (FileInfo f in images)
                flow.Add(Environment.MachineName, f.FullName);
            EmptyOverlay.Visibility = (flow.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
        }
        #endregion
        public TestWindow(string imageFolder)
        {
            InitializeComponent();
			LocalizeEmptyOverlay();
            flow.Cache = new ThumbnailManager();
            Load(imageFolder);
            slider.Minimum = 0;
            slider.Maximum = flow.Count - 1;
            double intervalSeconds = ReadSlideshowIntervalSecondsFromRegistry();
            slideshowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(intervalSeconds) };
            slideshowTimer.Tick += (s, e) =>
            {
                if (flow.Count == 0) return;
                if (flow.Count <= 1) return;
                int next = flow.Index + slideshowDirection;
                if (next >= flow.Count - 1)
                {
                    next = flow.Count - 1;
                    slideshowDirection = -1;
                }
                else if (next <= 0)
                {
                    next = 0;
                    slideshowDirection = 1;
                }
                flow.Index = next;
                if (slider.Value != flow.Index) slider.Value = flow.Index;
            };
        }
        private void LocalizeEmptyOverlay()
        {

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\\CoverShow"))
                {
                    if (key != null)
                    {
                        EmptyMessage.Text = key.GetValue("EmptyMessage_string") as string ?? EmptyMessage.Text;
						ChangeFolderButton.Content = key.GetValue("ChangeFolderButton_string") as string  ?? ChangeFolderButton.Content;
						ExitButton.Content = key.GetValue("ExitButton_string") as string  ?? ExitButton.Content;
						
						
                    }
                }
            }
            catch { }
        }
        private double ReadSlideshowIntervalSecondsFromRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\\CoverShow"))
                {
                    if (key != null)
                    {
                        var val = key.GetValue("SlideIntervalSecs") as string;
                        if (!string.IsNullOrEmpty(val) && double.TryParse(val, out double secs) && secs > 0.05)
                            return secs;
                    }
                }
            }
            catch { }
            return 2.0;
        }
        private DispatcherTimer slideshowTimer;
        private bool isSlideshowRunning;
        private int slideshowDirection = 1;
        private void ToggleSlideshow()
        {
            if (isSlideshowRunning)
            {
                slideshowTimer.Stop();
                isSlideshowRunning = false;
            }
            else
            {
                if (flow.Count == 0) return;
                // Reset to forward direction when starting
                slideshowDirection = 1;
                slideshowTimer.Start();
                isSlideshowRunning = true;
            }
        }
        private void ChangeFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select a folder containing images or mp3 files";
                dialog.ShowNewFolderButton = false;
                var result = dialog.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    flow.Index = 0;
                    Load(dialog.SelectedPath);
                    slider.Minimum = 0;
                    slider.Maximum = Math.Max(0, flow.Count - 1);
                }
            }
        }
        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }
    }
}
