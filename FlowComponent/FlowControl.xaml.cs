using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
namespace Wpf.CoverShow.FlowComponent
{
    public partial class FlowControl : UserControl
    {
        #region Fields
        public const int HalfRealizedCount = 7;
        public const int PageSize = HalfRealizedCount;
        private readonly ICoverFactory coverFactory;
        private readonly Dictionary<int, ImageInfo> imageList = new Dictionary<int, ImageInfo>();
        private readonly Dictionary<string, int> labelIndex = new Dictionary<string, int>();
        private readonly Dictionary<int, string> indexLabel = new Dictionary<int, string>();
        private readonly Dictionary<int, ICover> coverList = new Dictionary<int, ICover>();
        private int index;
        private int firstRealized = -1;
        private int lastRealized = -1;
        #endregion
        #region Private stuff
        private void RotateCover(int pos, bool animate)
        {
            if (coverList.ContainsKey(pos))
                coverList[pos].Animate(index, animate);
        }
        private void UpdateIndex(int newIndex)
        {
            if (index != newIndex)
            {
                bool animate = Math.Abs(newIndex - index) < PageSize;
                UpdateRange(newIndex);
                int oldIndex = index;
                index = newIndex;
                if (index > oldIndex)
                {
                    if (oldIndex < firstRealized)
                        oldIndex = firstRealized;
                    for (int i = oldIndex; i <= index; i++)
                        RotateCover(i, animate);
                }
                else
                {
                    if (oldIndex > lastRealized)
                        oldIndex = lastRealized;
                    for (int i = oldIndex; i >= index; i--)
                        RotateCover(i, animate);
                }
                camera.Position = new Point3D(Cover.CoverStep * index, camera.Position.Y, camera.Position.Z);
                UpdateSelectedFileName();
            }
        }
        private void viewPort_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var rayMeshResult = (RayMeshGeometry3DHitTestResult)VisualTreeHelper.HitTest(viewPort, e.GetPosition(viewPort));
            if (rayMeshResult != null)
            {
                int? hit = GetHitCoverIndex(rayMeshResult);
                if (hit.HasValue)
                {
                    if (hit.Value == index)
                        OpenCurrentFile();
                    else
                        UpdateIndex(hit.Value);
                }
            }
        }

        private void viewPort_MouseMove(object sender, MouseEventArgs e)
        {
            var rayMeshResult = (RayMeshGeometry3DHitTestResult)VisualTreeHelper.HitTest(viewPort, e.GetPosition(viewPort));
            if (rayMeshResult != null)
            {
                int? hit = GetHitCoverIndex(rayMeshResult);
                this.Cursor = (hit.HasValue && hit.Value == index) ? Cursors.Hand : Cursors.Arrow;
            }
            else
            {
                this.Cursor = Cursors.Arrow;
            }
        }

        private int? GetHitCoverIndex(RayMeshGeometry3DHitTestResult rayMeshResult)
        {
            foreach (int i in coverList.Keys)
            {
                if (!coverList.ContainsKey(i))
                    continue;
                if (coverList[i].Matches(rayMeshResult.MeshHit))
                    return i;
            }
            return null;
        }

        private void OpenCurrentFile()
        {
            if (imageList.Count == 0)
                return;
            int clampedIndex = Math.Max(0, Math.Min(index, imageList.Count - 1));
            var info = imageList[clampedIndex];
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = info.Path,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch
            {
                // Swallow errors to avoid crashing on invalid paths
            }
        }
        private void RemoveCover(int pos)
        {
            if (!coverList.ContainsKey(pos))
                return;
            coverList[pos].Destroy();
            coverList.Remove(pos);
        }
        private void UpdateRange(int newIndex)
        {
            int newFirstRealized = Math.Max(newIndex - HalfRealizedCount, 0);
            int newLastRealized = Math.Min(newIndex + HalfRealizedCount, imageList.Count - 1);
            if (lastRealized < newFirstRealized || firstRealized > newLastRealized)
            {
                visualModel.Children.Clear();
                coverList.Clear();
            }
            else if (firstRealized < newFirstRealized)
            {
                for (int i = firstRealized; i < newFirstRealized; i++)
                    RemoveCover(i);
            }
            else if (newLastRealized < lastRealized)
            {
                for (int i = lastRealized; i > newLastRealized; i--)
                    RemoveCover(i);
            }
            for (int i = newFirstRealized; i <= newLastRealized; i++)
            {
                if (!coverList.ContainsKey(i))
                {
                    ICover cover = coverFactory.NewCover(imageList[i].Host, imageList[i].Path, i, newIndex);
                    coverList.Add(i, cover);
                }
            }
            firstRealized = newFirstRealized;
            lastRealized = newLastRealized;
        }
        protected int FirstRealizedIndex
        {
            get { return firstRealized; }
        }
        protected int LastRealizedIndex
        {
            get { return lastRealized; }
        }
        private void Add(ImageInfo info)
        {
            imageList.Add(imageList.Count, info);
            UpdateRange(index);
            UpdateSelectedFileName();
        }
        #endregion
        public FlowControl()
        {
            InitializeComponent();
            coverFactory = new CoverFactory(visualModel);
			ApplyGradientColorsFromRegistry();
            baseCameraZ = camera.Position.Z;
        }
        private const double MinZoomScale = 0.5;
        private const double MaxZoomScale = 1.5;
        private double baseCameraZ;
        private double zoomScale = 1.0;
        public double ZoomScale
        {
            get { return zoomScale; }
            set
            {
                double clamped = Math.Max(MinZoomScale, Math.Min(MaxZoomScale, value));
                if (Math.Abs(clamped - zoomScale) > 0.0001)
                {
                    zoomScale = clamped;
                    camera.Position = new Point3D(camera.Position.X, camera.Position.Y, baseCameraZ / zoomScale);
                }
            }
        }
				private void ApplyGradientColorsFromRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\\CoverShow\\Theme"))
                {
                    if (key == null) return;
                    var top = key.GetValue("GradientTopColor") as string;
                    var bottom = key.GetValue("GradientBottomColor") as string;
					var fontColor = key.GetValue("FontColor") as string;
					var cameraPosition = key.GetValue("CameraPosition") as string;
                    var cameraLook = key.GetValue("CameraLook") as string;
                    var cameraUp = key.GetValue("CameraUp") as string;
                    var light1 = key.GetValue("Light1") as string;
					var light2 = key.GetValue("Light2") as string;
					var fontSize = key.GetValue("FontSize") as string;
                    var fontAlignment = key.GetValue("FontAlignment") as string;

                    if (!string.IsNullOrEmpty(top))
                    {
                        var topStop = (GradientStop)FindName("TopGradientStop");
                        if (topStop != null)
                            topStop.Color = (Color)ColorConverter.ConvertFromString(top);
                    }
                    if (!string.IsNullOrEmpty(bottom))
                    {
                        var bottomStop = (GradientStop)FindName("BottomGradientStop");
                        if (bottomStop != null)
                            bottomStop.Color = (Color)ColorConverter.ConvertFromString(bottom);
                    }
                    if (!string.IsNullOrEmpty(fontColor))
                    {
                        var font = (TextBlock)FindName("Label");
                        if (font != null)
                            font.Foreground = new System.Windows.Media.SolidColorBrush((Color)ColorConverter.ConvertFromString(fontColor));
                        font.FontSize = double.Parse(fontSize);
                        font.VerticalAlignment = (VerticalAlignment)Enum.Parse(typeof(VerticalAlignment), fontAlignment);
                    }
     


					if (!string.IsNullOrEmpty(cameraPosition))
                    {

                        double[] coords = cameraPosition
                            .Split(',')
                            .Select(s => double.Parse(s.Trim()))
                            .ToArray();

                        var camera = (PerspectiveCamera)FindName("camera");
                        if (camera != null)
                        cameraPosition.Split(',').ToArray();
                        camera.Position = new Point3D(coords[0], coords[1], coords[2]);
                    }
                    if (!string.IsNullOrEmpty(cameraLook))
                    {
                        double[] coords = cameraLook
                            .Split(',')
                            .Select(s => double.Parse(s.Trim()))
                            .ToArray();

                        var camera = (PerspectiveCamera)FindName("camera");
                        if (camera != null)
                            cameraLook.Split(',').ToArray();
                        camera.LookDirection = new Vector3D(coords[0], coords[1], coords[2]);
                    }
                    if (!string.IsNullOrEmpty(cameraUp))
                    {
                        double[] coords = cameraUp
                            .Split(',')
                            .Select(s => double.Parse(s.Trim()))
                            .ToArray();

                        var camera = (PerspectiveCamera)FindName("camera");
                        if (camera != null)
                            cameraUp.Split(',').ToArray();
                        camera.UpDirection = new Vector3D(coords[0], coords[1], coords[2]);
                    }
                    if (!string.IsNullOrEmpty(light1))
                    {
                        double[] coords = light1
                            .Split(',')
                            .Select(s => double.Parse(s.Trim()))
                            .ToArray();
                        var light1D = (DirectionalLight)FindName("Light1");
                        if (light1 != null)
                            light1.Split(',').ToArray();
                        light1D.Direction = new Vector3D(coords[0], coords[1], coords[2]);
                    }
					if (!string.IsNullOrEmpty(light2))
                    {
                        double[] coords = light2
                            .Split(',')
                            .Select(s => double.Parse(s.Trim()))
                            .ToArray();
                        var light2D = (DirectionalLight)FindName("Light2");
                        if (light2 != null)
                            light2.Split(',').ToArray();
                        light2D.Direction = new Vector3D(coords[0], coords[1], coords[2]);
                    }
                }
            }
            catch
            {
                // ignore invalid or missing registry values
            }
        }
        public static readonly System.Windows.DependencyProperty SelectedFileNameProperty =
            System.Windows.DependencyProperty.Register("SelectedFileName", typeof(string), typeof(FlowControl), new System.Windows.PropertyMetadata(string.Empty));

        public string SelectedFileName
        {
            get { return (string)GetValue(SelectedFileNameProperty); }
            set { SetValue(SelectedFileNameProperty, value); }
        }

        private void UpdateSelectedFileName()
        {
            if (imageList.Count == 0)
            {
                SelectedFileName = string.Empty;
                return;
            }
            int clampedIndex = Math.Max(0, Math.Min(index, imageList.Count - 1));
            var info = imageList[clampedIndex];
            SelectedFileName = Path.GetFileNameWithoutExtension(info.Path);
        }
        public IThumbnailManager Cache
        {
            set { Cover.Cache = value; }
        }
        public void GoToNext()
        {
            UpdateIndex(Math.Min(index + 1, imageList.Count - 1));
        }
        public void GoToPrevious()
        {
            UpdateIndex(Math.Max(index - 1, 0));
        }
        public void GoToNextPage()
        {
            UpdateIndex(Math.Min(index + PageSize, imageList.Count - 1));
        }
        public void GoToPreviousPage()
        {
            UpdateIndex(Math.Max(index - PageSize, 0));
        }
        public void ZoomBy(int wheelDelta)
        {
            double step = 0.05;
            double direction = Math.Sign(wheelDelta);
            ZoomScale = ZoomScale + direction * step;
        }
        public int Count
        {
            get { return imageList.Count; }
        }
        public int Index
        {
            get { return index; }
            set { UpdateIndex(value); }
        }
        public void Add(string host, string imagePath)
        {
            Add(new ImageInfo(host, imagePath));
        }
    }
}
