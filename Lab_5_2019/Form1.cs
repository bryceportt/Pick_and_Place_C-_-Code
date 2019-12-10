using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Lab_5_2019
{
    public partial class Form1 : Form
    {
        VideoCapture _capture;
        Thread _captureThread;
       // int counts = 0;

        public Form1()
        {
            InitializeComponent();
        }

        //initiaize serial port com
        SerialPort mySerialPort = new SerialPort("COM9");

        // function called when camera is opened
        private void ProcessImage()
        {
            while (_capture.IsOpened)
            {
                //capture camera frame and make a mat
                Mat sourceFrame = _capture.QueryFrame();

                // resize to PictureBox aspect ratio
                int newHeight = (sourceFrame.Size.Height * pictureBox1.Size.Width) / sourceFrame.Size.Width;
                Size newSize = new Size(pictureBox1.Size.Width, newHeight);
                CvInvoke.Resize(sourceFrame, sourceFrame, newSize);

                //flip frame to get correct pixel locations
                CvInvoke.Flip(sourceFrame, sourceFrame, FlipType.Vertical);

                // as a test for comparison, create a copy of the image with a binary filter:
                var binaryImage = sourceFrame.ToImage<Gray, byte>().ThresholdBinary(new Gray(125), new
                Gray(255)).Mat; // 125 to 255

                Image<Gray, byte> grayImage = sourceFrame.ToImage<Gray, byte>().ThresholdBinary(new Gray(125), new Gray(255));

                // Sample for gaussian blur:
                var blurredImage = new Mat();
                var cannyImage = new Mat();
                var decoratedImage = new Mat();
                CvInvoke.GaussianBlur(sourceFrame, blurredImage, new Size(9, 9), 0);

                // convert to B/W
                CvInvoke.CvtColor(blurredImage, blurredImage, typeof(Bgr), typeof(Gray));

                // apply canny:
                CvInvoke.Canny(blurredImage, cannyImage, 150, 255);

                CvInvoke.CvtColor(cannyImage, decoratedImage, typeof(Gray), typeof(Bgr));

                // find contours:
                using (VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint())
                {
                    // Build list of contours
                    CvInvoke.FindContours(cannyImage, contours, null, RetrType.List, ChainApproxMethod.ChainApproxSimple); // canny or grayimage
                    
                    int count = contours.Size;
                    string shape = "";

                    //for loop to find part type
                    for (int i = 0; i < contours.Size; i++)
                    {   //called to identify kind of shape
                        using (VectorOfPoint contour = contours[i])
                        using (VectorOfPoint approxContour = new VectorOfPoint())
                        {
                            CvInvoke.ApproxPolyDP(contour, approxContour, CvInvoke.ArcLength(contour, true) * 0.05, true);

                            if (CvInvoke.ContourArea(approxContour, false) > 250) //only consider contours with area greater than 250
                            {
                                if (approxContour.Size == 3) //The contour has 3 vertices, it is a triangle
                                {
                                    shape = "Triangle";
                                }
                                else if (approxContour.Size == 4)
                                {
                                    shape = "Square";
                                }
                                else
                                {
                                    continue;
                                }
                            }
                            int area1 = decoratedImage.Width * decoratedImage.Height;
                            Rectangle boundingBox = CvInvoke.BoundingRectangle(contours[i]);
                            int area2 = boundingBox.Width * boundingBox.Height;
                            double ares = CvInvoke.ContourArea(contour);

                            //discards the paper as a rectangle
                            if (area2 > area1 / 2)
                            {
                                continue;
                            }

                            // Draw on the display frame                        
                            MarkDetectedObject(sourceFrame, contours[i], boundingBox, CvInvoke.ContourArea(contour), shape);
                            Point center = new Point(boundingBox.X + boundingBox.Width / 2, boundingBox.Y + boundingBox.Height / 2);
                            Invoke(new Action(() =>
                            {
                                //prints calues to screen
                                label2.Text = $"Coordinates: (X){center.X}, (Y){center.Y}";
                            }));
                            count++;
                            //called to send needed values to arduino (if code doesnt work take ount count and 50)
                            if (CvInvoke.ContourArea(approxContour, false) > 1200)
                            {
                                SendValues(center.X, center.Y, shape);
                                Thread.Sleep(10000);
                            }
                            else
                                continue;
                        }
                    }

                    //prints importand screen information
                    Invoke(new Action(() =>
                    {
                        label1.Text = $" There are {contours.Size} contours detected";
                        label6.Text = $" Frame Width {sourceFrame.Width}";
                        label7.Text = $" Frame Height {sourceFrame.Height}";
                    }));
                   
                    //print Frames to screen
                    pictureBox1.Image = sourceFrame.Bitmap;
                }
            }
        }

        private void SendValues(double centerX, double centerY, string shape)
        {
            int Shape = 0;

            if (shape == "Square")
            {
                Shape = 1;
            }
            else if (shape == "Triangle")
            {
                Shape = 0;
            }

            // center for degree calculations
            double Width = 408;
            double Height = 306;

            // calculate mm 
            double x = (centerX * 12.5 * 25.4) / Width; // 12.5
            double y = (centerY * 8.5 * 25.4) / Height; // 8.5
            double Center = (12.5 * 25.4) / 2; //12.5

            //calculate theta and hypotenuse + 196.85 + 70 for distance to paper
            double y1 = y + 196.85 + 70; // for Degree. Degree is now correct
            double Distancefromcenter = Math.Abs(Center - x);
            double Degree = Math.Atan(Distancefromcenter / y1) * (180 / Math.PI);


            //calculating tool offset for distance
            double Distance = Math.Sqrt((Distancefromcenter * Distancefromcenter) + (y1 * y1));

            // subtract 105 because of end effector distance
            Distance = Distance - 105;

            if (x < Center)
            {
                Degree = 90 + Degree;
            }
            else if (x > Center)
            {
                Degree = 90 - Degree;
            }
            else if (x == Center)
            {
                Degree = 90;
            }

            // divide by two so its small enough to send over serial
            Distance = Distance / 2;

            // send bytes
            Invoke(new Action(() =>
            {
                // send Degree
                byte[] buffer = { (byte)Degree };
                mySerialPort.Write(buffer, 0, 1);

                //send distance
                byte[] buffer1 = { (byte)Distance };
                mySerialPort.Write(buffer1, 0, 1);

                //send shape
                byte[] buffer2 = { (byte)Shape };
                mySerialPort.Write(buffer2, 0, 1);
            }));
              
        }

        //function called to mark objects on frame
        private static void MarkDetectedObject(Mat frame, VectorOfPoint contour, Rectangle boundingBox, double area, string shape)
        {
            // Drawing contour and box around it 
            if (shape == "Square")
            {
                CvInvoke.Polylines(frame, contour, true, new Bgr(Color.Red).MCvScalar);
            }
            else
            {
                CvInvoke.Polylines(frame, contour, true, new Bgr(Color.Green).MCvScalar);
            }

            CvInvoke.Rectangle(frame, boundingBox, new Bgr(Color.Blue).MCvScalar);

            // Write information next to marked object            
            Point center = new Point(boundingBox.X + boundingBox.Width / 2, boundingBox.Y + boundingBox.Height / 2);
            var info = new string[] {
                    $"Area: {area}",
                    $"Position: {center.X}, {center.Y}",
                    $"Shape: {shape}"
            };
            WriteMultilineText(frame, info, new Point(center.X, boundingBox.Top + 12));

            CvInvoke.Circle(frame, center, 5, new Bgr(Color.Blue).MCvScalar, 5);
        }

        //function called to write on the frame postions
        private static void WriteMultilineText(Mat frame, string[] lines, Point origin)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                int y = i * 10 + origin.Y;

                // Moving down on each line                
                CvInvoke.PutText(frame, lines[i], new Point(origin.X, y), FontFace.HersheyPlain, 0.8, new Bgr(Color.Black).MCvScalar);
            }
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            //set up web cam
            _capture = new VideoCapture(1);
            _captureThread = new Thread(ProcessImage);
            _captureThread.Start();

            //serial open and set up
            mySerialPort.BaudRate = 9600;
            mySerialPort.Open();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            //end program
            _captureThread.Abort();
        }
    }
}
