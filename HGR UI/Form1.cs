using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Threading.Tasks;
using System.Windows.Forms;
using Accord.Imaging.Filters;
using Accord.Video;
using Accord.Video.DirectShow;
using SVM;

namespace HGR_UI
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private FilterInfoCollection CaptureDevice;

        private VideoCaptureDevice FinalFrame;

        private int x = 0, y = 0;

        private int label = 0, camera = 0, detection = 0, Skin = 0, OK = 0, nonblack = 0, training = 0;

        Bitmap prev, curr, newFrame;

        private void Form1_Load(object sender, EventArgs e)
        {
            Disconnect.Enabled = false;
            Start.Enabled = false;
            Stop.Enabled = false;
            CaptureDevice = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            foreach (FilterInfo Device in CaptureDevice)
            {
                VideoDevicesList.AddItem(Device.Name);
            }
            VideoDevicesList.selectedIndex = 0;
            FinalFrame = new VideoCaptureDevice();
        }

        private void Connect_Click(object sender, EventArgs e)
        {
            camera = 1;
            Connect.Enabled = false;
            Disconnect.Enabled = true;
            Start.Enabled = true;
            FinalFrame = new VideoCaptureDevice(CaptureDevice[VideoDevicesList.selectedIndex].MonikerString);
            FinalFrame.NewFrame += new NewFrameEventHandler(FinalFrame_NewFrame);
            FinalFrame.Start();
        }

        void FinalFrame_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            newFrame = (Bitmap)eventArgs.Frame.Clone();
            Video.Image = newFrame.Clone() as System.Drawing.Image;
        }

        private void Video_Click(object sender, EventArgs e)
        {
            if (camera == 0)
            {
                OpenFileDialog open = new OpenFileDialog();
                if (open.ShowDialog() == DialogResult.OK)
                {
                    Bitmap image = new Bitmap(open.FileName);
                    Video.Image = (Bitmap)image;
                    Detection();
                }
            }
            else
            {
                MessageBox.Show("Disconnect the camera", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
        }

        private void Disconnect_Click(object sender, EventArgs e)
        {
            camera = 0;
            Connect.Enabled = true;
            Disconnect.Enabled = false;
            Start.Enabled = false;
            Stop.Enabled = false;
            FinalFrame.Stop();
            Video.Image = null;
            SkinBox.Image = null;
            OutputText.Text = null;
            ExecutionTimeBox.Text = null;
            ProgressBar.Value = 0;
        }

        private async void Start_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                Stop.Enabled = true;
                Start.Enabled = false;
                detection = 1;
                await Image_Acquisition();
            }
            catch { }
        }

        public async Task Image_Acquisition()
        {
            while (detection == 1)
            {
                OK = 0;

                // Previous Frame
                prev = newFrame;

                //Delay of 0.1 second
                await Task.Delay(300);

                //Current Frame
                curr = newFrame;

                //Find the difference between the prev and curr
                ThresholdedDifference threshold = new ThresholdedDifference(28)
                {
                    OverlayImage = prev
                };

                //Resulted Binary Image
                curr = threshold.Apply(curr);

                //If there is a noticeable change, start detection
                nonblack = threshold.WhitePixelsCount;
                if (nonblack >= (curr.Width * curr.Height / 8)) OK = 1;
                if (OK == 1) Detection();

                //Delay of 0.1 seconds
                await Task.Delay(300);
            }
        }

        private void Detection()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            if (Video.Image != null)
            {
                if (ModeList.selectedIndex == 0)
                {
                    training = 1;
                    int prev = AlphabetList.selectedIndex;
                    if (AlphabetList.selectedIndex == 26 || prev == 26)
                        label = 67;
                    else if (AlphabetList.selectedIndex == -1)
                        label = prev;
                    else
                        label = AlphabetList.selectedIndex;
                }
                else
                    training = 0;


                ProgressBar.Visible = true;
               
                ProgressBar.Value = 0;
                ProgressBar.Maximum_Value = 9;
                ProgressBar.Value += 1;
                

                CapturedBox.Image = (Bitmap)Video.Image.Clone();
                Bitmap src = new Bitmap(CapturedBox.Image);

                //skin detection
                var image = new Rectangle(0, 0, src.Width, src.Height);
                var value = src.LockBits(image, ImageLockMode.ReadWrite, src.PixelFormat);
                var size = Bitmap.GetPixelFormatSize(value.PixelFormat) / 8;
                var buffer = new byte[value.Width * value.Height * size];
                Marshal.Copy(value.Scan0, buffer, 0, buffer.Length);

                System.Threading.Tasks.Parallel.Invoke(
                    () =>
                    {
                        Skin_process(buffer, 0, 0, value.Width / 2, value.Height / 2, value.Width, size);
                    },
                    () =>
                    {
                        Skin_process(buffer, 0, value.Height / 2, value.Width / 2, value.Height, value.Width, size);
                    },
                    () =>
                    {
                        Skin_process(buffer, value.Width / 2, 0, value.Width, value.Height / 2, value.Width, size);
                    },
                    () =>
                    {
                        Skin_process(buffer, value.Width / 2, value.Height / 2, value.Width, value.Height, value.Width, size);
                    }
                );
                Marshal.Copy(buffer, 0, value.Scan0, buffer.Length);
                src.UnlockBits(value);
                SkinBox.Image = src;


                if (Skin == 1)
                {
                    ProgressBar.Value += 1;

                    //Dilation & Erosion
                    src = Grayscale.CommonAlgorithms.BT709.Apply(src);
                    BinaryDilation3x3 dilatation = new BinaryDilation3x3();
                    BinaryErosion3x3 erosion = new BinaryErosion3x3();
                    for (int a = 1; a <= 10; a++)
                        src = dilatation.Apply(src);
                    for (int a = 1; a <= 10; a++)
                        src = erosion.Apply(src);

                    ProgressBar.Value += 1;
                    NoiseBox.Image = src;

                    //Blob
                    try
                    {
                        ExtractBiggestBlob blob = new ExtractBiggestBlob();
                        src = blob.Apply(src);
                        x = blob.BlobPosition.X;
                        y = blob.BlobPosition.Y;
                        ProgressBar.Value += 1;
                    }
                    catch
                    {
                        this.Show();
                        //MessageBox.Show("Lightning conditions are not good for detecting the gestures", "Bad Lights", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }

                    //Merge
                    Bitmap srcImage = new Bitmap(CapturedBox.Image);
                    Bitmap dstImage = new Bitmap(src);
                    var srcrect = new Rectangle(0, 0, srcImage.Width, srcImage.Height);
                    var dstrect = new Rectangle(0, 0, dstImage.Width, dstImage.Height);
                    var srcdata = srcImage.LockBits(srcrect, ImageLockMode.ReadWrite, srcImage.PixelFormat);
                    var dstdata = dstImage.LockBits(dstrect, ImageLockMode.ReadWrite, dstImage.PixelFormat);
                    var srcdepth = Bitmap.GetPixelFormatSize(srcdata.PixelFormat) / 8;
                    var dstdepth = Bitmap.GetPixelFormatSize(dstdata.PixelFormat) / 8;
                    //bytes per pixel
                    var srcbuffer = new byte[srcdata.Width * srcdata.Height * srcdepth];
                    var dstbuffer = new byte[dstdata.Width * dstdata.Height * dstdepth];
                    //copy pixels to buffer
                    Marshal.Copy(srcdata.Scan0, srcbuffer, 0, srcbuffer.Length);
                    Marshal.Copy(dstdata.Scan0, dstbuffer, 0, dstbuffer.Length);

                    System.Threading.Tasks.Parallel.Invoke(
                        () =>
                        {
                            //upper-left
                            Merge_process(srcbuffer, dstbuffer, x, 0, y, 0, x + (dstdata.Width / 2), dstdata.Width / 2, y + (dstdata.Height / 2), dstdata.Height / 2, srcdata.Width, dstdata.Width, srcdepth, dstdepth);
                        },
                        () =>
                        {
                            //upper-right
                            Merge_process(srcbuffer, dstbuffer, x + (dstdata.Width / 2), dstdata.Width / 2, y, 0, x + (dstdata.Width), dstdata.Width, y + (dstdata.Height / 2), dstdata.Height / 2, srcdata.Width, dstdata.Width, srcdepth, dstdepth);
                        },
                        () =>
                        {
                            //lower-left
                            Merge_process(srcbuffer, dstbuffer, x, 0, y + (dstdata.Height / 2), dstdata.Height / 2, x + (dstdata.Width / 2), dstdata.Width / 2, y + (dstdata.Height), dstdata.Height, srcdata.Width, dstdata.Width, srcdepth, dstdepth);
                        },
                        () =>
                        {
                            //lower-right
                            Merge_process(srcbuffer, dstbuffer, x + (dstdata.Width / 2), dstdata.Width / 2, y + (dstdata.Height / 2), dstdata.Height / 2, x + (dstdata.Width), dstdata.Width, y + (dstdata.Height), dstdata.Height, srcdata.Width, dstdata.Width, srcdepth, dstdepth);
                        }
                    );

                    //Copy the buffer back to image
                    Marshal.Copy(srcbuffer, 0, srcdata.Scan0, srcbuffer.Length);
                    Marshal.Copy(dstbuffer, 0, dstdata.Scan0, dstbuffer.Length);
                    srcImage.UnlockBits(srcdata);
                    dstImage.UnlockBits(dstdata);
                    src = dstImage;
                    ProgressBar.Value += 1;
                    CropBox.Image = src;


                    //Resize
                    ResizeBilinear resize = new ResizeBilinear(200, 200);
                    src = resize.Apply(src);
                    ProgressBar.Value += 1;

                    //Edges
                    src = Grayscale.CommonAlgorithms.BT709.Apply((Bitmap)src);
                    SobelEdgeDetector edges = new SobelEdgeDetector();
                    src = edges.Apply(src);
                    ProgressBar.Value += 1;
                    EdgeDetectorBox.Image = src;

                    //HOEF
                    Bitmap block = new Bitmap(src);
                    int[] edgescount = new int[50];
                    double[] norm = new double[200];
                    String text = null;
                    int sum = 0;
                    int z = 1;
                    for (int p = 1; p <= 6; p++)
                    {
                        for (int q = 1; q <= 6; q++)
                        {
                            for (int x = (p - 1) * block.Width / 6; x < (p * block.Width / 6); x++)
                            {
                                for (int y = (q - 1) * block.Height / 6; y < (q * block.Height / 6); y++)
                                {
                                    Color colorPixel = block.GetPixel(x, y);

                                    int r = colorPixel.R;
                                    int g = colorPixel.G;
                                    int b = colorPixel.B;

                                    if (r != 0 & g != 0 & b != 0)
                                        edgescount[z]++;
                                }
                            }
                            z++;
                        }
                    }

                    for (z = 1; z <= 36; z++) sum = sum + edgescount[z];
                    for (z = 1; z <= 36; z++)
                    {
                        norm[z] = (double)edgescount[z] / sum;
                        text = text + " " + z.ToString() + ":" + norm[z].ToString();
                    }

                    if (training == 1)
                    {
                        File.AppendAllText(@"D:\train.txt", label.ToString() + text + Environment.NewLine);
                        ProgressBar.Value += 1;
                    }
                    else
                    {
                        File.WriteAllText(@"D:\test.txt", label.ToString() + text + Environment.NewLine);
                        ProgressBar.Value += 1;


                        //SVM
                        Problem train = Problem.Read(@"D:\train.txt");
                        Problem test = Problem.Read(@"D:\test.txt");
                        Parameter parameter = new Parameter()
                        {
                            C = 32,
                            Gamma = 8
                        };
                        Model model = Training.Train(train, parameter);
                        Prediction.Predict(test, @"D:\result.txt", model, false);
                        int value1 = Convert.ToInt32(File.ReadAllText(@"D:\result.txt"));
            

                        String alphabet = null;
                        if (value1 == 27)
                        {
                            alphabet += "Welcome  ";
                        }

                        else if (value1 == 28)
                            alphabet += "Good Morning";
                        else if (value1 == 29)
                            alphabet += "Thank You";
                        else
                            alphabet += (char)(65 + value1);

                     
                        OutputText.Text = alphabet;
                        SpeechSynthesizer speechSynthesizer = new SpeechSynthesizer();
                        speechSynthesizer.SetOutputToDefaultAudioDevice();
                        speechSynthesizer.Volume = 100;
                        speechSynthesizer.Rate = -2;
                        speechSynthesizer.SelectVoiceByHints(VoiceGender.Female, VoiceAge.Child);
                        speechSynthesizer.SpeakAsync(alphabet);

                        if (alphabet == " ")
                            speechSynthesizer.SpeakAsync(OutputText.Text);
                        ProgressBar.Value += 1;
                    }
                }
                else
                    this.Show();
                watch.Stop();
                var time = (watch.ElapsedMilliseconds);
                float secs = (float)time / 1000;
                ExecutionTimeBox.Text = Convert.ToString(secs) + " " + "Seconds";

            }
        }

        public void Skin_process(byte[] buffer, int x, int y, int X, int Y, int length, int size)
        {
            for (int i = x; i < X; i++)
            {
                for (int j = y; j < Y; j++)
                {
                    var displacement = ((j * length) + i) * size;
                    var r = buffer[displacement + 2];
                    var g = buffer[displacement + 1];
                    var b = buffer[displacement + 0];
                    if (r >= 45 & r <= 255 & g > 34 & g <= 229 & b >= 15 & b <= 200 & r - g >= 11 & r - b >= 15 & g - b >= 4 & r > g & r > b & g > b)
                    {
                        Skin = 1;
                        buffer[displacement + 0] = buffer[displacement + 1] = buffer[displacement + 2] = 255;
                    }
                    else
                        buffer[displacement + 0] = buffer[displacement + 1] = buffer[displacement + 2] = 0;
                }
            }

        }

        public void Merge_process(byte[] srbuffer, byte[] dsbuffer, int srcx, int dstx, int srcy, int dsty, int srcendx, int dstendx, int srcendy, int dstendy, int srcwidth, int dstwidth, int srdepth, int dsdepth)
        {

            try
            {
                for (int i = srcx, m = dstx; (i < srcendx & m < dstendx); i++, m++)
                {
                    for (int j = srcy, n = dsty; (j < srcendy & n < dstendy); j++, n++)
                    {
                        var offset = ((j * srcwidth) + i) * srdepth;
                        var offset1 = ((n * dstwidth) + m) * dsdepth;

                        var srcB = srbuffer[offset + 0];
                        var srcG = srbuffer[offset + 1];
                        var srcR = srbuffer[offset + 2];

                        var dstB = dsbuffer[offset1 + 0];
                        var dstG = dsbuffer[offset1 + 1];
                        var dstR = dsbuffer[offset1 + 2];

                        if (dstR != 0 & dstG != 0 & dstB != 0)
                        {
                            dsbuffer[offset1 + 0] = srbuffer[offset + 0];
                            dsbuffer[offset1 + 1] = srbuffer[offset + 1];
                            dsbuffer[offset1 + 2] = srbuffer[offset + 2];
                        }
                    }
                }
            }
            catch { }

        }

        private void Stop_Click(object sender, EventArgs e)
        {
            detection = 0;
            Stop.Enabled = false;
            Start.Enabled = true;
        }

        private void Reset_Click(object sender, EventArgs e)
        {
            Video.Image = null;
            CapturedBox.Image = null;
            SkinBox.Image = null;
            NoiseBox.Image = null;
            CropBox.Image = null;
            EdgeDetectorBox.Image = null;
            OutputText.Text = "";
        }
    }
}
