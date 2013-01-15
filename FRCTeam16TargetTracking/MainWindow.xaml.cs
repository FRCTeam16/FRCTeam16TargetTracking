using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Emgu.CV;
using Microsoft.Kinect;
using System.Windows.Media;
using Emgu.CV.Structure;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Media.Imaging;
using System.Drawing.Imaging;

/*
 TODO: 
 * ADD FORM ELEMENTS FOR FINE TUNING DETECTION - (DONE)
 * LOGIC TO DIFFERENTIATE BETWEEN GOALS
 * LOGIC TO GET DEPTH/ERROR TO GOALS
 * SUPPORT FOR WORKER CANCELLATION - (DONE)
 * SAVE FORM PROPERTIES TO SETTINGS FILE
 * CODE CLEAN UP
*/

namespace FRCTeam16TargetTracking
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private class FormPropBag
        {
            public Bitmap KImg { get; set; }
            public string ImgFileName { get; set; }
            public bool ColorFilter { get; set; }
            public double BMin { get; set; }
            public double BMax { get; set; }
            public double GMin { get; set; }
            public double GMax { get; set; }
            public double RMin { get; set; }
            public double RMax { get; set; }
            public double Threshold { get; set; }
            public double ThresholdLinking { get; set; }
            public double MinArea { get; set; }
            public double ApproxPoly { get; set; }
        }

        private class DepthFramePropBag
        {
            public short[] RawData { get; set; }
            public byte[] Pixels { get; set; }
            public int DepthFrameWidth { get; set; }
            public int DepthFrameHeight { get; set; }
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hobject);

        private BackgroundWorker _processWorker;
        private BackgroundWorker _depthWorker;
        private List<MCvBox2D> _boxList = new List<MCvBox2D>();
        private bool _runningImaging = false;
        private bool _runningDepth = false;
        private Image<Bgr, byte> _orgImg;
        private Bitmap _contourImg;
        private KinectSensor _kinectSensor;
        private System.Windows.Threading.DispatcherTimer _imageRefreshTimer; 

        private const string FORM_TITLE = "Image Processing";

        private void bw_ProcessImage(object sender, DoWorkEventArgs e)
        {
            if (_processWorker.CancellationPending)
            {
                _runningImaging = false;
                e.Cancel = true;
                return;
            }

            _runningImaging = true;
            FormPropBag formProps = (FormPropBag)e.Argument;

            if (_orgImg != null)
            {
                _orgImg.Dispose();
                _orgImg = null;
            }

            if (formProps.KImg != null)
            {
                _orgImg = new Image<Bgr, byte>(formProps.KImg).PyrDown().PyrUp();
            }
            else
            {
                _orgImg = new Image<Bgr, byte>(formProps.ImgFileName).PyrDown().PyrUp();
            }
            _orgImg._SmoothGaussian(3);

            Image<Gray, byte> img = null;

            if (formProps.ColorFilter)
            {
                img = _orgImg.InRange(new Bgr(formProps.BMin, formProps.GMin, formProps.RMin), new Bgr(formProps.BMax, formProps.GMax, formProps.RMax));
                //img = img.PyrDown().PyrUp();
                //img._SmoothGaussian(3);
            }
            else
            {
                img = _orgImg.Convert<Gray, byte>();
                
            }

            Gray cannyThreshold = new Gray(formProps.Threshold);
            Gray cannyThresholdLinking = new Gray(formProps.ThresholdLinking);
            using (Image<Gray, Byte> cannyEdges = img.Canny(cannyThreshold, cannyThresholdLinking))
            {
                _contourImg = cannyEdges.ToBitmap();
                using (MemStorage storage = new MemStorage())
                for (
                    Contour<System.Drawing.Point> contours = cannyEdges.FindContours(
                        Emgu.CV.CvEnum.CHAIN_APPROX_METHOD.CV_CHAIN_APPROX_SIMPLE,
                        Emgu.CV.CvEnum.RETR_TYPE.CV_RETR_LIST,
                        storage);
                    contours != null;
                    contours = contours.HNext)
                {
                    Contour<System.Drawing.Point> currentContour = contours.ApproxPoly(formProps.ApproxPoly, storage);

                    if (currentContour.Total == 4)
                    {
                        if (currentContour.Area > formProps.MinArea)
                        {
                            if (currentContour.BoundingRectangle.Width > currentContour.BoundingRectangle.Height)
                            {
                                bool isRectangle = true;
                                System.Drawing.Point[] pts = currentContour.ToArray();
                                LineSegment2D[] edges = Emgu.CV.PointCollection.PolyLine(pts, true);

                                for (int i = 0; i < edges.Length; i++)
                                {
                                    double angle = Math.Abs(edges[(i + 1) % edges.Length].GetExteriorAngleDegree(edges[i]));

                                    if (angle < 85 || angle > 95)
                                    {
                                        isRectangle = false;
                                        break;
                                    }
                                }
                                if (isRectangle)
                                {
                                    _boxList.Add(currentContour.GetMinAreaRect());
                                }
                            }
                        }
                    }
                }
            }

            e.Result = img;
        }

        private void bw_ProcessComplete(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled) 
            {
                return; 
            }

            image2.Source = sourceFromBitmap(_orgImg.ToBitmap());
            using (Image<Bgr, Byte> rectangleImage = _orgImg.CopyBlank())
            {
                foreach (MCvBox2D box in _boxList)
                {
                    rectangleImage.Draw(box, new Bgr(System.Drawing.Color.DarkOrange), 2);
                }
                
                if ((bool)!chkShowContours.IsChecked) {
                    image1.Source = sourceFromBitmap(rectangleImage.ToBitmap());
                }
                else
                {
                    image1.Source = sourceFromBitmap(_contourImg);
                }
                _runningImaging = false;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            SetFormDefaults();
            StartKinectSensor();
        }

        private void StartKinectSensor()
        {
            if (KinectSensor.KinectSensors.Count > 0)
            {
                _kinectSensor = KinectSensor.KinectSensors[0];
                _kinectSensor.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30);
                _kinectSensor.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);
                _kinectSensor.AllFramesReady += new EventHandler<AllFramesReadyEventArgs>(newSensor_AllFramesReady);
                _kinectSensor.Start();

                Title = FORM_TITLE + " - Kinect Connected";
            }
            else
            {
                Title = FORM_TITLE + " - No Kinect Found";
                chkOpenImage.IsChecked = true;
                chkOpenImage.IsEnabled = false;
                chkShowDepth.IsEnabled = false;
                lblSelectImage1.Visibility = System.Windows.Visibility.Visible;
                lblSelectImage2.Visibility = System.Windows.Visibility.Visible;
            }
        }

        private void SetFormDefaults()
        {
            txtApproxPoly.Text = "10";
            txtThreshold.Text = "100";
            txtThresholdLinking.Text = "120";
            txtMinArea.Text = "2500";
        }

        private System.Drawing.Bitmap BitmapFromSource(BitmapSource bitmapsource)
        {
            Bitmap bmp = new Bitmap(bitmapsource.PixelWidth, bitmapsource.PixelHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            BitmapData data = bmp.LockBits(new Rectangle(System.Drawing.Point.Empty, bmp.Size), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            bitmapsource.CopyPixels(Int32Rect.Empty, data.Scan0, data.Height * data.Stride, data.Stride);
            bmp.UnlockBits(data);

            return bmp;
        }

        private BitmapSource sourceFromBitmap(System.Drawing.Bitmap bmp)
        {
            IntPtr hBitmap = bmp.GetHbitmap();
            BitmapSource source;

            try
            {
                source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hBitmap);
            }

            return source;
        }

        private void sdrRMin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            txtRMin.Text = sdrRMin.Value.ToString();
        }

        private void sdrRMax_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            txtRMax.Text = sdrRMax.Value.ToString();
        }

        private void sdrGMin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            txtGMin.Text = sdrGMin.Value.ToString();
        }

        private void sdrGMax_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            txtGMax.Text = sdrGMax.Value.ToString();
        }

        private void sdrBMin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            txtBMin.Text = sdrBMin.Value.ToString();
        }

        private void sdr_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            txtBMax.Text = sdrBMax.Value.ToString();
        }

        void newSensor_AllFramesReady(object sender, AllFramesReadyEventArgs e)
        {
            using (ColorImageFrame colorFrame = e.OpenColorImageFrame())
            {
                if (_runningImaging)
                {
                    return;
                }
                if (colorFrame == null)
                {
                    return;
                }

                if (_orgImg != null)
                {
                    _orgImg.Dispose();
                    _orgImg = null;
                }

                byte[] pixels = new byte[colorFrame.PixelDataLength];
                colorFrame.CopyPixelDataTo(pixels);

                int stride = colorFrame.Width * 4;
                BitmapSource bs = System.Windows.Media.Imaging.BitmapSource.Create(colorFrame.Width, colorFrame.Height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);

                InitImageProcess(BitmapFromSource(bs));
            }

            using (DepthImageFrame depthFrame = e.OpenDepthImageFrame())
            {
                if (_runningDepth)
                {
                    return;
                }
                if (depthFrame == null)
                {
                    return;
                }

                short[] rawDepthData = new short[depthFrame.PixelDataLength];
                depthFrame.CopyPixelDataTo(rawDepthData);

                byte[] pixels = new byte[depthFrame.Height * depthFrame.Width * 4];

                DepthFramePropBag props = new DepthFramePropBag();
                props.RawData = rawDepthData;
                props.Pixels = pixels;
                props.DepthFrameHeight = depthFrame.Height;
                props.DepthFrameWidth = depthFrame.Width;

                _depthWorker = new BackgroundWorker();
                _depthWorker.DoWork += new DoWorkEventHandler(bw_DepthProcessImage);
                _depthWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bw_DepthProcessComplete);
                _depthWorker.WorkerSupportsCancellation = true;
                _depthWorker.RunWorkerAsync((object)props);
            }
        }

        private void bw_DepthProcessImage(object sender, DoWorkEventArgs e)
        {
            if (_depthWorker.CancellationPending)
            {
                _runningDepth = false;
                e.Cancel = true;
                return;
            }
            _runningDepth = true;
            DepthFramePropBag props = (DepthFramePropBag)e.Argument;
            short[] rawDepthData = props.RawData;
            byte[] pixels = props.Pixels;
    
            const int BlueIndex = 0;
            const int GreenIndex = 1;
            const int RedIndex = 2;

            for (int depthIndex = 0, colorIndex = 0; depthIndex < rawDepthData.Length && colorIndex < pixels.Length; depthIndex++, colorIndex += 4)
            {
                int depth = rawDepthData[depthIndex] >> DepthImageFrame.PlayerIndexBitmaskWidth;

                if (MMToFeet(depth) < 3)
                {
                    //unknown
                    continue;
                }
                else if (MMToFeet(depth) >= 3 && MMToFeet(depth) <= 6)
                {
                    pixels[colorIndex + BlueIndex] = 255;
                    pixels[colorIndex + GreenIndex] = 0;
                    pixels[colorIndex + RedIndex] = 0;
                }
                else if (MMToFeet(depth) > 6 && MMToFeet(depth) <= 10)
                {

                    pixels[colorIndex + BlueIndex] = 0;
                    pixels[colorIndex + GreenIndex] = 255;
                    pixels[colorIndex + RedIndex] = 0;
                }

                else if (MMToFeet(depth) > 10 && MMToFeet(depth) <= 13)
                {

                    pixels[colorIndex + BlueIndex] = 0;
                    pixels[colorIndex + GreenIndex] = 0;
                    pixels[colorIndex + RedIndex] = 255;
                }

                props.Pixels = pixels;
                e.Result = props;
            }
        }

        private void bw_DepthProcessComplete(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                return;
            }

            DepthFramePropBag props = (DepthFramePropBag)e.Result;

            int stride = props.DepthFrameWidth * 4;
            image3.Source = System.Windows.Media.Imaging.BitmapSource.Create(props.DepthFrameWidth, props.DepthFrameHeight, 96, 96, PixelFormats.Bgr32, null, props.Pixels, stride);

            _runningDepth = false;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopKinectSensor(_kinectSensor);
        }

        private void StopKinectSensor(KinectSensor sensor)
        {
            if (sensor != null)
            {
                if (sensor.IsRunning)
                {
                    sensor.Stop();

                    if (sensor.AudioSource != null)
                    {
                        sensor.AudioSource.Stop();
                    }
                }
            }
        }

        private void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.DefaultExt = ".png";
            dlg.Filter = "PNG Files (*.png)|*.png|JPG Files (*.jpg)|*.jpg";
            dlg.Multiselect = false;

            if ((bool)dlg.ShowDialog())
            {
                lblSelectImage1.Visibility = System.Windows.Visibility.Hidden;
                lblSelectImage2.Visibility = System.Windows.Visibility.Hidden;
                StopKinectSensor(_kinectSensor);
                txtFileName.Text = dlg.FileName;
                _imageRefreshTimer = new System.Windows.Threading.DispatcherTimer();
                _imageRefreshTimer.Tick += new EventHandler(timer_tick);
                _imageRefreshTimer.Interval = new TimeSpan(0, 0, 1);
                _imageRefreshTimer.Start();
            }
        }

        private void timer_tick(object sender, EventArgs e)
        {
            if (!_runningImaging)
            {
                InitImageProcess(null);
            }
        }

        private void InitImageProcess(Bitmap img)
        {
            _boxList.Clear();

            FormPropBag fpb = new FormPropBag();
            fpb.KImg = img;
            fpb.ImgFileName = txtFileName.Text;
            fpb.ColorFilter = (bool)chkColorFilter.IsChecked;
            fpb.BMin = sdrBMin.Value;
            fpb.BMax = sdrBMax.Value;
            fpb.GMin = sdrGMin.Value;
            fpb.GMax = sdrGMax.Value;
            fpb.RMin = sdrRMin.Value;
            fpb.RMax = sdrRMax.Value;
            
            if (txtThreshold.Text.Length > 0) { 
                fpb.Threshold = Convert.ToDouble(txtThreshold.Text); 
            } 
            else 
            { 
                fpb.Threshold = 0;
            }
            if (txtThresholdLinking.Text.Length > 0) { fpb.ThresholdLinking = Convert.ToDouble(txtThresholdLinking.Text); } else { fpb.ThresholdLinking = 0; }
            if (txtMinArea.Text.Length > 0) { fpb.MinArea = Convert.ToDouble(txtMinArea.Text); } else { fpb.MinArea = 0; }
            if (txtApproxPoly.Text.Length > 0) { fpb.ApproxPoly = Convert.ToDouble(txtApproxPoly.Text); } else { fpb.ApproxPoly = 0; }

            _processWorker = new BackgroundWorker();
            _processWorker.DoWork += new DoWorkEventHandler(bw_ProcessImage);
            _processWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bw_ProcessComplete);
            _processWorker.WorkerSupportsCancellation = true;
            _processWorker.RunWorkerAsync((object)fpb);
        }

        private double MMToFeet(int mm)
        {
            return mm * .003281;
        }

        private void chkShowDepth_Click(object sender, RoutedEventArgs e)
        {
            if ((bool)chkShowDepth.IsChecked)
            {
                image3.Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                image3.Visibility = System.Windows.Visibility.Hidden;
            }
        }

        private void chkOpenImage_Click(object sender, RoutedEventArgs e)
        {
            _processWorker.CancelAsync();
            _depthWorker.CancelAsync();

            if ((bool)chkOpenImage.IsChecked)
            {
                StopKinectSensor(_kinectSensor);
                lblSelectImage1.Visibility = System.Windows.Visibility.Visible;
                lblSelectImage2.Visibility = System.Windows.Visibility.Visible;
                btnBrowse.IsEnabled = true;
                chkShowDepth.IsEnabled = false;
            }
            else
            {
                if (_imageRefreshTimer != null) { _imageRefreshTimer.Stop(); }
                StartKinectSensor();
                lblSelectImage1.Visibility = System.Windows.Visibility.Hidden;
                lblSelectImage2.Visibility = System.Windows.Visibility.Hidden;
                btnBrowse.IsEnabled = false;
                txtFileName.Text = "";
                chkShowDepth.IsEnabled = false;
            }
        }

        private void txtThreshold_LostFocus(object sender, RoutedEventArgs e)
        {
            if (txtThreshold.Text.Length == 0)
            {
                txtThreshold.Text = _thresholdTemp;
            }
        }

        private void txtThresholdLinking_LostFocus(object sender, RoutedEventArgs e)
        {
            if (txtThresholdLinking.Text.Length == 0)
            {
                txtThresholdLinking.Text = _thresholdLinkingTemp;
            }
        }

        private void txtMinArea_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtMinArea.Text.Length == 0)
            {
                txtMinArea.Text = _minAreaTemp;
            }
        }

        private void txtApproxPoly_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtApproxPoly.Text.Length == 0)
            {
                txtApproxPoly.Text = _approxPolyTemp;
            }
        }

        private string _thresholdTemp;
        private void txtThreshold_GotFocus(object sender, RoutedEventArgs e)
        {
            _thresholdTemp = txtThreshold.Text;
        }

        private string _thresholdLinkingTemp;
        private void txtThresholdLinking_GotFocus(object sender, RoutedEventArgs e)
        {
            _thresholdLinkingTemp = txtThresholdLinking.Text;
        }

        private string _minAreaTemp;
        private void txtMinArea_GotFocus(object sender, RoutedEventArgs e)
        {
            _minAreaTemp = txtMinArea.Text;
        }

        private string _approxPolyTemp;
        private void txtApproxPoly_GotFocus(object sender, RoutedEventArgs e)
        {
            _approxPolyTemp = txtApproxPoly.Text;
        }
    }
}
