/*
 *  Simple proof-of-concept for OpenCvSharp Inspection System
 *  
 *  ToDo:
 *      - add seperate window for real time camera view
 *  
 *  Date:   8/8/24
 *  Author: John Glatts
 */
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using DirectShowLib;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.Intrinsics;
using System;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Rebar;
using System.Drawing;
using System.Security.Cryptography;

namespace TestOpenCVSharp
{
    public partial class Form1 : Form
    {
        private CancellationTokenSource cancelTokenSource;
        private CancellationToken token;
        private VideoCapture capture;
        private Mat frame;
        private Bitmap image;
        private bool useBlackWhite;
        private int camIndex;
        private int threshold_value;
        private int threshold_max_value;
        private int canny_thresh1;
        private int canny_thresh2;

        public Form1()
        {
            InitializeComponent();
            camIndex = 1;
            useBlackWhite = false;
            cancelTokenSource = new CancellationTokenSource();
            openCam();
            btnStart_Click(null, null);
        }

        private void listCamDevices()
        {
            String s = "\n";
            DsDevice[] devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);

            if (devices.Length == 0)
            {
                MessageBox.Show("no video devices!");
                return;
            }
            foreach (DsDevice dsDevice in devices)
            {
                s += dsDevice.Name + "\n";
            }
            MessageBox.Show("num devices: " + devices.Length + s);
        }

        private void tryOpenCam()
        {
            frame = new Mat();
            openCam();

            if (capture.IsOpened())
            {
                capture.Read(frame);
                image = BitmapConverter.ToBitmap(frame);
                if (mainFeedPicBox.Image != null)
                {
                    mainFeedPicBox.Image.Dispose();
                }
                mainFeedPicBox.Image = image;
            }
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            try
            {
                cancelTokenSource.Cancel();
                cancelTokenSource.Dispose();
            }
            catch (Exception ex) { }
            cancelTokenSource = new CancellationTokenSource();
            token = cancelTokenSource.Token;
            Task task = new Task(startLiveFeed, token);
            task.Start();
        }

        private void openCam()
        {
            try
            {
                capture = new VideoCapture(camIndex);
                capture.Open(camIndex);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message + "\n" + ex.StackTrace + "\n" + ex.InnerException);
                return;
            }
        }

        private void startLiveFeed()
        {
            frame = new Mat();

            if (!capture.IsOpened())
            {
                MessageBox.Show("not open!");
                return;
            }

            // forever loop to run the webcam
            while (true)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }
                getAndDisplayFrame(5);
            }
        }

        private void getAndDisplayFrame(int delay)
        {
            capture.Read(frame);
            if (useBlackWhite)
            {
                Mat new_frame = new Mat();
                Cv2.CvtColor(frame, new_frame, ColorConversionCodes.BGR2GRAY);
                frame = new_frame;
            }
            drawCrossHair(frame, 200);
            updateLiveFeedImage(frame);
            Task.Delay(delay);
        }

        private void drawCrossHair(Mat frame, int line_size) 
        {
            int vert_y1 = (frame.Rows - line_size) / 2;
            int vert_y2 = vert_y1 + line_size;
            int horz_x1 = (frame.Cols - line_size) / 2;
            int horz_x2 = horz_x1 + line_size;

            Cv2.Line(frame, frame.Cols/2, vert_y1, frame.Cols/2, vert_y2, new Scalar(255, 255), thickness:4);
            Cv2.Line(frame, horz_x1, frame.Rows/2, horz_x2, frame.Rows/2, new Scalar(255, 255), thickness:4);
        }

        private void detectGap()
        {
            Mat src_gray = new Mat();
            Mat src_canny = new Mat();
            Mat src_roi = new Mat();
            Mat src_thresh = new Mat();
            OpenCvSharp.Point[][] contours;
            HierarchyIndex[] hierarchyIndexes;

            capture.Read(frame);
            src_roi = frame;
            // double check if this split is needed
            /*
            src_roi = frame.SubMat(new OpenCvSharp.Range(50, frame.Rows),
                                   new OpenCvSharp.Range(0, frame.Cols));
            */
            if (src_roi == null)
                return;

            updateLiveFeedImage(src_roi);
            Task.Delay(2000);

            Cv2.CvtColor(src_roi, src_gray, ColorConversionCodes.BGR2GRAY);
            updateLiveFeedImage(src_gray);
            Task.Delay(2000);

            if (!updateThreshValues())
                return;

            Cv2.Threshold(src_gray, src_thresh, threshold_value, threshold_max_value, ThresholdTypes.Binary);
            updateLiveFeedImage(src_thresh);
            Task.Delay(2000);

            // fine tune these params
            Cv2.Canny(src_thresh, src_canny, canny_thresh1, canny_thresh2, 3, false);
            updateLiveFeedImage(src_canny);
            Task.Delay(2000);

            /*
             *  See below, can use FindCountours as the gap finder
             *  Will need to fine tune this guy
             */
            Cv2.FindContours(src_canny, out contours, out hierarchyIndexes,
                             mode: RetrievalModes.External,
                             method: ContourApproximationModes.ApproxSimple);
            //MessageBox.Show("Found # " + contours.Length.ToString() + " contours");

            Mat contured_mat = new Mat(src_gray.Rows, src_gray.Cols, MatType.CV_8UC1);
            Cv2.DrawContours(contured_mat, contours, -1, new Scalar(255, 255), thickness: 3, hierarchy: hierarchyIndexes);
            Cv2.ImShow("tt", contured_mat);

            Mat contured_mat_2 = new Mat(src_gray.Rows, src_gray.Cols, MatType.CV_8UC1);
            for (int j = 0; j < contours.Length; j++)
            {
                if (contours[j].Length > 20)
                { 
                    Cv2.DrawContours(contured_mat_2, contours, j, new Scalar(255, 255), 
                                    thickness: -1, hierarchy: hierarchyIndexes);
                }
            }
            Cv2.ImShow("t2t", contured_mat_2);

            //anaylzeFrame(src_roi, src_canny);
        }

        private bool updateThreshValues()
        {
            if (!Int32.TryParse(txtBoxThreshHoldVal.Text, out threshold_value))
                 return false;
            if (!Int32.TryParse(txtBoxThreshMaxVal.Text, out threshold_max_value))
                return false;
            if (!Int32.TryParse(txtBoxCannyThresh1.Text, out canny_thresh1))
                return false;
            if (!Int32.TryParse(txtBoxCannyThresh2.Text, out canny_thresh2))
                return false;
            return true;
        }

        private void anaylzeFrame(Mat src_roi, Mat src_canny)
        {
            // splits the image into 2 seperate ROI's
            // find contours of both ROI's and stores in 2 sets
            // run convex hull on both sets of contours
            // find distance between the convex hulls
            // draw both sets on OG img to examine
            // thats gap!
            /*
                CPP Source
                Mat src_gray, src_thresh;
                Mat roi_left, roi_right;
                vector<vector<Point>> contours_set_one, contours_set_two;
                vector<Point> convex_hull_points_one, convex_hull_points_two;

                roi_left = src_canny(Range(0, src_canny.size[0]), Range(0, 47));
                roi_right = src_canny(Range(0, src_canny.size[0]), Range(47, src_canny.size[1]));
                findContours(roi_left, contours_set_one, RETR_EXTERNAL, CHAIN_APPROX_SIMPLE);
                findContours(roi_right, contours_set_two, RETR_EXTERNAL, CHAIN_APPROX_SIMPLE);
                convex_hull_points_one = contoursConvexHull(contours_set_one);
                convex_hull_points_two = contoursConvexHull(contours_set_two);
                translateContours(convex_hull_points_two, 47);
            
                Rect rect_left = boundingRect(convex_hull_points_one);
                Rect rect_right = boundingRect(convex_hull_points_two);
                rectangle(src_roi, rect_left, Scalar(0, 0, 255), 2);
                rectangle(src_roi, rect_right, Scalar(0, 0, 255), 2);
                int rect_left_pos = rect_left.x + rect_left.width;
                int rect_right_pos = rect_right.x;
                int center_pos = (rect_left_pos + rect_right_pos) / 2;

                cout << "The center pos is " << center_pos << endl;
                line(src_roi, Point(rect_right_pos, 40), Point(rect_left_pos, 40), Scalar(0, 0, 255), 1);
                line(src_roi, Point(center_pos, 0), Point(center_pos, roi_left.size[0]), Scalar(255, 0, 0), 1);
                cout << "Distance to left-edge " << center_pos - rect_left.x << endl;
                cout << "Distance to right-edge " << (rect_right.x + rect_right.width) - center_pos << endl;
            */
            Mat src_gray = new Mat();
            Mat src_thresh = new Mat();
            Mat roi_left = new Mat();
            Mat roi_right = new Mat();
            Mat out_left = new Mat();
            Mat out_right = new Mat();
            OpenCvSharp.Point[][] contoursSetOne;
            HierarchyIndex[] hierarchyIndexesOne;
            OpenCvSharp.Point[][] contoursSetTwo;
            HierarchyIndex[] hierarchyIndexesTwo;
            int middle_split = src_canny.Cols / 2;

            roi_left = src_canny.SubMat(new OpenCvSharp.Range(0, src_canny.Rows),
                                        new OpenCvSharp.Range(0, middle_split));

            roi_right = src_canny.SubMat(new OpenCvSharp.Range(0, src_canny.Rows),
                                         new OpenCvSharp.Range(middle_split, src_canny.Cols));

            //Cv2.ImShow("left", roi_left);
            //Cv2.ImShow("right", roi_right);

            Cv2.FindContours(roi_left, out contoursSetOne, out hierarchyIndexesOne,
                             mode: RetrievalModes.External,
                             method: ContourApproximationModes.ApproxSimple);

            Cv2.FindContours(roi_right, out contoursSetTwo, out hierarchyIndexesTwo,
                             mode: RetrievalModes.External,
                             method: ContourApproximationModes.ApproxSimple);

            getAddHullPoints(roi_left, contoursSetOne);
            getAddHullPoints(roi_right, contoursSetOne);

            Cv2.ImShow("left-hull", roi_left);
            Cv2.ImShow("right-hull", roi_right);
        }

        private void getAddHullPoints(Mat frame, OpenCvSharp.Point[][] contours)
        {
            for (int i = 0; i < contours.Length; i++)
            {
                List<OpenCvSharp.Point> points = new List<OpenCvSharp.Point>();
                for (int j = 0; j < contours[i].Length; j++)
                {
                    points.Add(contours[i][j]);
                }
                OpenCvSharp.Point[] out_hull = Cv2.ConvexHull(points);
                for (int k = 0; k < out_hull.Length; k++)
                {
                    Cv2.Circle(frame, out_hull[k].X, out_hull[k].Y, 1, new Scalar(255, 0), thickness: 3);
                }
            }
        }

        private void updateLiveFeedImage(Mat frame) 
        {
            image = BitmapConverter.ToBitmap(frame);
            if (mainFeedPicBox.Image != null)
            {
                mainFeedPicBox.Image.Dispose();
            }
            mainFeedPicBox.Image = image;
        }

        private void btnStop_Click_1(object sender, EventArgs e)
        {
            try
            {
                cancelTokenSource.Cancel();
                cancelTokenSource.Dispose();
            }
            catch (Exception ex) { }
        }

        private void btnFindGap_Click(object sender, EventArgs e)
        {
            try
            {
                cancelTokenSource.Cancel();
                cancelTokenSource.Dispose();
            }
            catch (Exception ex) { }
            Task.Delay(10);
            detectGap();
        }

        private void radioBtnBlackWhite_CheckedChanged(object sender, EventArgs e)
        {
            useBlackWhite = radioBtnBlackWhite.Checked == true;
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            
        }
    }
}
