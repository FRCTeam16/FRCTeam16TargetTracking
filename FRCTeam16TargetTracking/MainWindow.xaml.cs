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
            public bool ColorFilter { get; set; }
            public double BMin { get; set; }
            public double BMax { get; set; }
            public double GMin { get; set; }
            public double GMax { get; set; }
            public double RMin { get; set; }
            public double RMax { get; set; }
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hobject);

        private BackgroundWorker bw;
        private List<MCvBox2D> boxList = new List<MCvBox2D>();
        private bool running = false;
        private int cnt = 0;
        private Image<Bgr, byte> img;
        private Bitmap contImg;

        private void bw_ProcessImage(object sender, DoWorkEventArgs e)
        {
            running = true;
            FormPropBag formProps = (FormPropBag)e.Argument;

            if (img != null)
            {
                img.Dispose();
                img = null;
            }

            img = new Image<Bgr, byte>(formProps.KImg).PyrDown().PyrUp();
            img._SmoothGaussian(5);

            Image<Gray, byte> img2 = null;

            if (formProps.ColorFilter)
            {
                img2 = img.InRange(new Bgr(formProps.BMin, formProps.GMin, formProps.RMin), new Bgr(formProps.BMax, formProps.GMax, formProps.RMax));
                img2 = img2.PyrDown().PyrUp();
                img2._SmoothGaussian(3);
            }
            else
            {
                img2 = img.Convert<Gray, byte>();
                
            }

            //using (Image<Gray, Byte> gray = img.Convert<Gray, Byte>().PyrDown().PyrUp())
            //{
                Gray cannyThreshold = new Gray(100);
                Gray cannyThresholdLinking = new Gray(150);
                using (Image<Gray, Byte> cannyEdges = img2.Canny(cannyThreshold, cannyThresholdLinking))
                {
                    contImg = cannyEdges.ToBitmap();
                    //contImg = img2.ToBitmap();
                    using (MemStorage storage = new MemStorage())
                    for (
                        Contour<System.Drawing.Point> contours = cannyEdges.FindContours(
                            Emgu.CV.CvEnum.CHAIN_APPROX_METHOD.CV_CHAIN_APPROX_SIMPLE,
                            Emgu.CV.CvEnum.RETR_TYPE.CV_RETR_LIST,
                            storage);
                        contours != null;
                        contours = contours.HNext)
                    {
                        Contour<System.Drawing.Point> currentContour = contours.ApproxPoly(contours.Perimeter * 0.05, storage);

                        if (currentContour.Total == 4)
                        {
                            if (currentContour.Area > 250)
                            {
                                if (currentContour.BoundingRectangle.Width > currentContour.BoundingRectangle.Height)
                                {
                                    bool isRectangle = true;
                                    System.Drawing.Point[] pts = currentContour.ToArray();
                                    LineSegment2D[] edges = Emgu.CV.PointCollection.PolyLine(pts, true);

                                    for (int i = 0; i < edges.Length; i++)
                                    {
                                        double angle = Math.Abs(edges[(i + 1) % edges.Length].GetExteriorAngleDegree(edges[i]));

                                        if (angle < 80 || angle > 100)
                                        {
                                            isRectangle = false;
                                            break;
                                        }
                                    }
                                    if (isRectangle)
                                    {
                                        boxList.Add(currentContour.GetMinAreaRect());
                                    }
                                }
                            }
                        }
                    }
                }

                e.Result = img;
            //}
        }

        private void bw_ProcessComplete(object sender, RunWorkerCompletedEventArgs e)
        {
            image2.Source = sourceFromBitmap(img.ToBitmap());
            using (Image<Bgr, Byte> triangleRectangleImage = img.CopyBlank())
            {
                foreach (MCvBox2D box in boxList)
                {
                    triangleRectangleImage.Draw(box, new Bgr(System.Drawing.Color.DarkOrange), 2);
                }
                
                if ((bool)!chkShowContours.IsChecked) {
                    image1.Source = sourceFromBitmap(triangleRectangleImage.ToBitmap());
                }
                else
                {
                    image1.Source = sourceFromBitmap(contImg);
                }
                running = false;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            kinectSensorChooser1.KinectSensorChanged += new DependencyPropertyChangedEventHandler(kinectSensorChooser1_KinectSensorChanged);
        }

        void kinectSensorChooser1_KinectSensorChanged(object sender, DependencyPropertyChangedEventArgs e)
        {

            var oldSensor = (KinectSensor)e.OldValue;
            if (oldSensor != null)
            {
                oldSensor.Stop();
                oldSensor.AudioSource.Stop();
            }

            var sensor = (KinectSensor)e.NewValue;
            if (sensor == null)
            {
                return;
            }

            sensor.DepthStream.Enable(DepthImageFormat.Resolution320x240Fps30);
            sensor.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);
            sensor.SkeletonStream.Enable(); //required to see players
            sensor.AllFramesReady += new EventHandler<AllFramesReadyEventArgs>(newSensor_AllFramesReady);

            try
            {
                sensor.Start();
            }
            catch (System.IO.IOException)
            {
                kinectSensorChooser1.AppConflictOccurred();
            }
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
                if (running)
                {
                    return;
                }
                if (colorFrame == null)
                {
                    return;
                }

                if (img != null)
                {
                    img.Dispose();
                    img = null;
                }

                byte[] pixels = new byte[colorFrame.PixelDataLength];
                colorFrame.CopyPixelDataTo(pixels);

                int stride = colorFrame.Width * 4;
                BitmapSource bs = System.Windows.Media.Imaging.BitmapSource.Create(colorFrame.Width, colorFrame.Height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);

                boxList.Clear();

                bw = new BackgroundWorker();
                bw.DoWork += new DoWorkEventHandler(bw_ProcessImage);
                bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bw_ProcessComplete);

                FormPropBag fpb = new FormPropBag();
                fpb.KImg = BitmapFromSource(bs);
                fpb.ColorFilter = (bool)chkColorFilter.IsChecked;
                fpb.BMin = sdrBMin.Value;
                fpb.BMax = sdrBMax.Value;
                fpb.GMin = sdrGMin.Value;
                fpb.GMax = sdrGMax.Value;
                fpb.RMin = sdrRMin.Value;
                fpb.RMax = sdrRMax.Value;

                bw.RunWorkerAsync((object)fpb);
                cnt++;
                label1.Content = cnt;
            }

           /* using (DepthImageFrame depthFrame = e.OpenDepthImageFrame())
            {
                if (depthFrame == null)
                {
                    return;
                }


                byte[] pixels = GenerateColoredBytes(depthFrame);

                //number of bytes per row width * 4 (B,G,R,Empty)
                int stride = depthFrame.Width * 4;

                image1.Source = System.Windows.Media.Imaging.BitmapSource.Create(depthFrame.Width, depthFrame.Height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
                

                //testing
                //Image<Gray, ushort> img = new Image<Gray, ushort>(640, 480, 640 * 2, depthFrame.); 

                
            }*/
           // System.Threading.Thread.Sleep(10);
        }

        /*private byte[] GenerateColoredBytes(DepthImageFrame depthFrame)
        {

            //get the raw data from kinect with the depth for every pixel
            short[] rawDepthData = new short[depthFrame.PixelDataLength];
            depthFrame.CopyPixelDataTo(rawDepthData);

            //use depthFrame to create the image to display on-screen
            //depthFrame contains color information for all pixels in image
            //Height x Width x 4 (Red, Green, Blue, empty byte)
            Byte[] pixels = new byte[depthFrame.Height * depthFrame.Width * 4];

            //hardcoded locations to Blue, Green, Red (BGR) index positions       
            const int BlueIndex = 0;
            const int GreenIndex = 1;
            const int RedIndex = 2;

            for (int depthIndex = 0, colorIndex = 0; depthIndex < rawDepthData.Length && colorIndex < pixels.Length; depthIndex++, colorIndex += 4)
            {
                //get the player (requires skeleton tracking enabled for values)
                int player = rawDepthData[depthIndex] & DepthImageFrame.PlayerIndexBitmask;

                //gets the depth value
                int depth = rawDepthData[depthIndex] >> DepthImageFrame.PlayerIndexBitmaskWidth; //shift bits to right by 3

                //if (MMToFeet(depth) <= .1)
                //{
                //    var g = MMToFeet(depth);
                //    pixels[colorIndex + BlueIndex] = 255;
                //    pixels[colorIndex + GreenIndex] = 255;
                //    pixels[colorIndex + RedIndex] = 0;
                //}
                if (MMToFeet(depth) <= 3)
                {
                    pixels[colorIndex + BlueIndex] = 255;
                    pixels[colorIndex + GreenIndex] = 0;
                    pixels[colorIndex + RedIndex] = 0;

                }
                else if (MMToFeet(depth) > 3 && MMToFeet(depth) <= 6)
                {
                    pixels[colorIndex + BlueIndex] = 0;
                    pixels[colorIndex + GreenIndex] = 255;
                    pixels[colorIndex + RedIndex] = 0;
                }
                else if (MMToFeet(depth) > 6 && MMToFeet(depth) <= 10)
                {

                    pixels[colorIndex + BlueIndex] = 0;
                    pixels[colorIndex + GreenIndex] = 0;
                    pixels[colorIndex + RedIndex] = 255;
                }

                else if (MMToFeet(depth) > 10 && MMToFeet(depth) <= 13)
                {

                    pixels[colorIndex + BlueIndex] = 255;
                    pixels[colorIndex + GreenIndex] = 255;
                    pixels[colorIndex + RedIndex] = 0;
                }

                //Color all players "gold"
                if (player > 0)
                {
                    pixels[colorIndex + BlueIndex] = Colors.Gold.B;
                    pixels[colorIndex + GreenIndex] = Colors.Gold.G;
                    pixels[colorIndex + RedIndex] = Colors.Gold.R;
                }

            }


            return pixels;
        }*/

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopKinect(kinectSensorChooser1.Kinect);
        }

        private void StopKinect(KinectSensor sensor)
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

       /* private double MMToFeet(int mm)
        {
            return mm * .003281;
        }*/
    }
}
