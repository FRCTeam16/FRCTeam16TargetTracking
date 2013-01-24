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
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;

/*
 TODO: 
 * ADD FORM ELEMENTS FOR FINE TUNING DETECTION - (DONE)
 * LOGIC TO DIFFERENTIATE BETWEEN GOALS (DONE - Targets Ordered Highest to Lowest)
 * LOGIC TO GET DEPTH/ERROR TO GOALS (DONE - Will Need Tweaking When Able To Test)
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
            public bool ProcessImage { get; set; }
        }

        private class TargetDetail
        {
            //nothing here yet
        }

        private class DepthFramePropBag
        {
            public short[] RawData { get; set; }
            public byte[] Pixels { get; set; }
            public int DepthFrameWidth { get; set; }
            public int DepthFrameHeight { get; set; }
            public DepthImagePixel[] DepthImagePixels;
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
        private ColorImagePoint[] _mappedDepthLocations;

        private const string FORM_TITLE = "Image Processing";
        private System.Drawing.Color TARGET_OUTLINE_COLOR = System.Drawing.Color.DarkOrange;
        private const int TARGET_OUTLINE_THICKNESS = 3;

        private Form1 _debugWnd = new Form1();
        private StringBuilder _debugInfo = new StringBuilder();
        private StringBuilder _targetInfo = new StringBuilder();
        private Stopwatch _stopwatch = new Stopwatch();

        private void bw_ProcessImage(object sender, DoWorkEventArgs e)
        {
            FormPropBag formProps = (FormPropBag)e.Argument;

            if (_processWorker.CancellationPending || !formProps.ProcessImage)
            {
                _runningImaging = false;
                e.Cancel = true;
                return;
            }

            _runningImaging = true;

            //if (_orgImg != null)
            //{
            //    _orgImg.Dispose();
            //    _orgImg = null;
            //}
            _stopwatch.Start();

            WriteToDebug("Process Image Begin");
            if (formProps.KImg != null)
            {
                _orgImg = new Image<Bgr, byte>(formProps.KImg).PyrDown().PyrUp();
            }
            else
            {
                _orgImg = new Image<Bgr, byte>(formProps.ImgFileName).PyrDown().PyrUp();
            }

            WriteToDebug("Applying Gaussian Blur");
          //  _orgImg._SmoothGaussian(3);
            _orgImg = _orgImg.Dilate(9);
           // _orgImg = _orgImg.Erode(5);
            
            Image<Gray, byte> img = null;

            if (formProps.ColorFilter)
            {
                WriteToDebug("Applying Color Filtering");
                img = _orgImg.InRange(new Bgr(formProps.BMin, formProps.GMin, formProps.RMin), new Bgr(formProps.BMax, formProps.GMax, formProps.RMax));
            }
            else
            {
                WriteToDebug("Converting Image To Grayscale");
                img = _orgImg.Convert<Gray, byte>();
            }

            Gray cannyThreshold = new Gray(formProps.Threshold);
            Gray cannyThresholdLinking = new Gray(formProps.ThresholdLinking);
            using (Image<Gray, Byte> cannyEdges = img.Canny(cannyThreshold, cannyThresholdLinking))
            {
                _contourImg = cannyEdges.ToBitmap();
                WriteToDebug("Filter Contours Start");

                _boxList = ImageFilter.FilterContours(cannyEdges, formProps.MinArea, formProps.ApproxPoly);
            }

            WriteToDebug("Filtered Contours Found: " + _boxList.Count);
            e.Result = img;
        }

        private void bw_ProcessComplete(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled) 
            {
                return; 
            }

            image2.Source = sourceFromBitmap(_orgImg.ToBitmap());
            //System.Drawing.Color c = System.Drawing.Color.DarkOrange;
            using (Image<Bgr, Byte> rectangleImage = _orgImg)
            {
               /* WriteToDebug("Highlighting Targets");
                int i = 1;

                //Parallel.ForEach(_boxList, box =>
                foreach(MCvBox2D box in _boxList)
                {
                    MCvFont f = new MCvFont(Emgu.CV.CvEnum.FONT.CV_FONT_HERSHEY_COMPLEX, 1.0, 1.0);
                    _orgImg.Draw(i.ToString(), ref f, new System.Drawing.Point((int)box.center.X - 10, (int)box.center.Y + 10), new Bgr(TARGET_OUTLINE_COLOR));

                    _orgImg.Draw(box, new Bgr(TARGET_OUTLINE_COLOR), TARGET_OUTLINE_THICKNESS);
                    i++;
                }*/

                if ((bool)chkShowContours.IsChecked)
                {
                    image1.Source = sourceFromBitmap(_contourImg);
                    image4.Visibility = Visibility.Hidden;
                    image1.Visibility = Visibility.Visible;
                }
                else
                {
                    //image4.Source = sourceFromBitmap(_orgImg.ToBitmap()); 
                    image4.Visibility = Visibility.Visible;
                    image1.Visibility = Visibility.Hidden;
                }

                WriteToDebug("Image Process Complete (" + _stopwatch.Elapsed.TotalMilliseconds + ")");

                _stopwatch.Reset();
                _stopwatch.Stop();

                _debugWnd.SetDebugInfo(_debugInfo.ToString());
                _debugInfo.Length = 0;
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
                lblSelectImage1.Visibility = System.Windows.Visibility.Visible;
                lblSelectImage2.Visibility = System.Windows.Visibility.Visible;
            }
        }

        private void SetFormDefaults()
        {
            txtApproxPoly.Text = "10";
            txtThreshold.Text = "100";
            txtThresholdLinking.Text = "120";
            txtMinArea.Text = "250";

            chkBypassTargetStream.IsEnabled = (bool)chkProcessImage.IsChecked;
            
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
            byte[] pixels;
            using (ColorImageFrame colorFrame = e.OpenColorImageFrame())
            {
                if (_runningImaging || _runningDepth)
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

                _runningImaging = true;

                WriteToDebug("All Frames Ready - Color");

                pixels = new byte[colorFrame.PixelDataLength];
                colorFrame.CopyPixelDataTo(pixels);
                BitmapSource bs = System.Windows.Media.Imaging.BitmapSource.Create(colorFrame.Width, colorFrame.Height, 96, 96, PixelFormats.Bgr32, null, pixels, colorFrame.Width * 4);
                //if (!(bool)chkProcessImage.IsChecked)
                //{
                    image2.Source = bs;
                //}

                InitImageProcess(BitmapFromSource(bs));
            }

            if (_runningDepth || pixels == null || (_boxList.Count == 0 && !(bool)chkBypassTargetStream.IsChecked))
            {
                return;
            }

            using (DepthImageFrame depthFrame = e.OpenDepthImageFrame())
            {
                if (depthFrame == null)
                {
                    return;
                }

                _runningDepth = true;

                WriteToDebug("All Frames Ready - Depth");

                short[] rawDepthData = new short[depthFrame.PixelDataLength];
                DepthImagePixel[] dip = new DepthImagePixel[depthFrame.PixelDataLength];
                _mappedDepthLocations = new ColorImagePoint[depthFrame.PixelDataLength];
                depthFrame.CopyPixelDataTo(rawDepthData);

                DepthFramePropBag props = new DepthFramePropBag();
                props.RawData = rawDepthData;
                props.DepthImagePixels = dip;
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
            //_runningDepth = true;
            DepthFramePropBag props = (DepthFramePropBag)e.Argument;
           
            byte[] bitmapBits = props.Pixels;

            _kinectSensor.CoordinateMapper.MapDepthFrameToColorFrame(DepthImageFormat.Resolution640x480Fps30, props.DepthImagePixels, ColorImageFormat.RgbResolution640x480Fps30, _mappedDepthLocations);

            for (int i = 0; i < props.RawData.Length; i++)
            //Parallel.For(0, props.RawData.Length, i =>
            {
                int depthVal = props.RawData[i] >> DepthImageFrame.PlayerIndexBitmaskWidth;
                ColorImagePoint point = _mappedDepthLocations[i];

                if (_boxList.Count > 0)
                {
                    int j = 1;
                    foreach (MCvBox2D box in _boxList)
                    {

                        if ((Convert.ToInt32(box.center.X) == point.X) && (Convert.ToInt32((box.center.Y - (box.size.Height / 2) - 10)) == point.Y))
                        {
                            if (depthVal >= 609)
                            {
                                Random rnd = new Random();
                                int rndNum = rnd.Next();
                                _targetInfo.Append("box " + j + " Depth: " + depthVal.ToString() + "(Feet:" + MMToFeet(depthVal) + ")" + "\n");
                            }
                        }

                        j++;
                    }
                }
            }

            props.Pixels = bitmapBits;
            e.Result = props;

            /*
            3-5 | 914.4-1524
            5-7 | 1524-2133.6
            7-9 | 2133.6-2743.2
            9-11 | 2743.2-3352.8
            11-13 | 3352.8-3962.4
            */
        }

        private void bw_DepthProcessComplete(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                return;
            }
            
            DepthFramePropBag props = (DepthFramePropBag)e.Result;
            BitmapSource bs = System.Windows.Media.Imaging.BitmapSource.Create(props.DepthFrameWidth, props.DepthFrameHeight, 96, 96, PixelFormats.Bgr32, null, props.Pixels, props.DepthFrameWidth * 4);
            using (Image<Bgr, Byte> rectangleImage = new Image<Bgr, byte>(BitmapFromSource(bs)))
            {
                int i = 1;
                foreach (MCvBox2D box in _boxList)
                //Parallel.ForEach(_boxList, box =>
                {
                    MCvFont f = new MCvFont(Emgu.CV.CvEnum.FONT.CV_FONT_HERSHEY_COMPLEX, 1.0, 1.0);
                    rectangleImage.Draw(i.ToString(), ref f, new System.Drawing.Point((int)box.center.X - 10, (int)box.center.Y + 10), new Bgr(TARGET_OUTLINE_COLOR));
                    rectangleImage.Draw(box, new Bgr(TARGET_OUTLINE_COLOR), TARGET_OUTLINE_THICKNESS);

                    MCvBox2D dummy = new MCvBox2D(new System.Drawing.PointF(box.center.X, (box.center.Y - (box.size.Height / 2) - 10)), new System.Drawing.SizeF(1f, 1f), 90.0f);
                    rectangleImage.Draw(dummy, new Bgr(System.Drawing.Color.Red), 10);
                    i++;
                }

                image4.Source = sourceFromBitmap(rectangleImage.ToBitmap());
                _debugWnd.SetTargetInfo(_targetInfo.ToString());
                _targetInfo.Length = 0;
           }

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
                chkProcessImage.IsChecked = true;
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
            //_boxList.Clear();

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
            fpb.ProcessImage = (bool)chkProcessImage.IsChecked;
            
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

        private double InchesToMM(int mm)
        {
            return mm / 0.039370;
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
            }
            else
            {
                if (_imageRefreshTimer != null) { _imageRefreshTimer.Stop(); }
                StartKinectSensor();
                lblSelectImage1.Visibility = System.Windows.Visibility.Hidden;
                lblSelectImage2.Visibility = System.Windows.Visibility.Hidden;
                btnBrowse.IsEnabled = false;
                txtFileName.Text = "";
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

        private void chkShowContours_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void Window_Loaded_1(object sender, RoutedEventArgs e)
        {
            SetFormDefaults();
            StartKinectSensor();
        }

        private void btnOpenDebug_Click(object sender, RoutedEventArgs e)
        {
            if (_debugWnd != null)
            {
                _debugWnd.Show();
            }
        }

        private void WriteToDebug(string debug)
        {
            _debugInfo.Append(debug + " (" + _stopwatch.Elapsed.TotalMilliseconds + ")\n");
        }

        private void chkProcessImage_Click(object sender, RoutedEventArgs e)
        {
            if ((bool)chkProcessImage.IsChecked)
            {
                chkBypassTargetStream.IsEnabled = true;
            }
            else
            {
                chkBypassTargetStream.IsEnabled = false;
                chkBypassTargetStream.IsChecked = false;
            }
        }
    }
}
