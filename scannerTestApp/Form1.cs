using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ZXing;
using Tesseract;
using System.Text.RegularExpressions;
using System.Drawing.Imaging;

namespace ScannerTestApp
{
    public partial class Form1 : Form
    {
        private const string BarcodeSeparator = "102030405060708090";
        private List<string> selectedFiles = new List<string>();
        private ListBox filesListBox; 

        public Form1()
        {
            InitializeComponent();
            InitializeCustomComponents();
        }

        private void InitializeCustomComponents()
        {
            Button fetchFilesButton = new Button { Text = "Fetch Files", Dock = DockStyle.Top };
            filesListBox = new ListBox { Dock = DockStyle.Fill };
            Button processButton = new Button { Text = "Process Files", Dock = DockStyle.Bottom };

            fetchFilesButton.Click += (sender, e) => FetchFilesFromDirectory();
            processButton.Click += (sender, e) => Separator();

            Controls.Add(filesListBox);
            Controls.Add(processButton);
            Controls.Add(fetchFilesButton);
        }

        private void FetchFilesFromDirectory()
        {
            // Specify the directory path from configuration
            string directoryPath = ConfigurationManager.AppSettings["TempFileSaveLocation"];

            if (Directory.Exists(directoryPath))
            {
                selectedFiles.Clear();
                string[] files = Directory.GetFiles(directoryPath, "*.tif");

                filesListBox.Items.Clear(); 
                selectedFiles.AddRange(files);
                foreach (var file in files)
                {
                    filesListBox.Items.Add(Path.GetFileName(file));
                }
            }
            else
            {
                MessageBox.Show("Directory does not exist.");
            }
        }

        public void Separator()
        {
            var lst = new List<string>();
            var barcodeReader = new BarcodeReader();
            var strList = new StringBuilder();

            foreach (var filePath in selectedFiles)
            {
                var filename = Path.GetFileName(filePath);
                var bitmap = (Bitmap)Image.FromFile(filePath);
                try
                {
                    using (bitmap)
                    {
                        var result = barcodeReader.Decode(bitmap);
                        if (result != null)
                        {
                            if (!BarcodeSeparator.Equals(result.Text))
                            {
                                Bitmap preBarcodeProcessing = PreprocessForBarcode(bitmap);
                                result = barcodeReader.Decode(preBarcodeProcessing);

                            }
                            if(BarcodeSeparator.Equals(result.Text))
                            {
                                var Text = ReadText(filePath, filename, bitmap);
                                if (Text == "InsuranceDataServicesSeparatorSheet")
                                {
                                    if (strList.Length > 0)
                                    {
                                        lst.Add(strList.ToString().TrimEnd(','));
                                        strList.Clear();
                                    }
                                }
                            }
                        }
                        else
                        {
                            lst.Add($"{filePath}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error processing file {filename}: {ex.Message}");
                }
            }

            if (strList.Length > 0)
            {
                lst.Add(strList.ToString().TrimEnd(','));
            }

            //MoveFiles(lst);
        }
        public Bitmap PreprocessForBarcode(Bitmap file)
        {
                Bitmap resized = new Bitmap(800, 600);
                using (Graphics g = Graphics.FromImage(resized))
                {
                    g.DrawImage(file, 0, 0, 800, 600);
                }
                return resized;
        }
        public string ReadText(string filepath, string filename ,Bitmap image)
        {
            try
            {
                string tessDataPath = ConfigurationManager.AppSettings["TessDataPath"];
                string language = ConfigurationManager.AppSettings["language"];

                if (string.IsNullOrWhiteSpace(tessDataPath) || string.IsNullOrWhiteSpace(language))
                {
                    throw new InvalidOperationException("Tesseract configuration is missing.");
                }
                var processedImage = PreprocessImageForOCR(filepath);
                using (var engine = new TesseractEngine(tessDataPath, language, EngineMode.Default))
                {
                    engine.SetVariable("debug_file", "tesseract_log.txt");
                    using (var img = Pix.LoadFromFile(filepath))
                    {
                            using (var page = engine.Process(img))
                            {
                                string text = page.GetText();
                                string textWithoutWhitespace = Regex.Replace(text, @"\s+", "");
                                return textWithoutWhitespace;
                            }
                    }
                }
            }
            catch(Exception ex){
                throw ex;
            }
            
        }
        public Bitmap PreprocessImageForOCR(string filepath)
        {
            Bitmap bitmap = (Bitmap)Bitmap.FromFile(filepath);
            if (!IsImage32DPP(bitmap))
            {
               bitmap =  ConvertImageTo32DPI(bitmap);
            }
            bitmap = ConvertToGrayscale(bitmap);
            bitmap = BinarizeImage(bitmap);
            return bitmap;
        }

        public static bool IsImage32DPP(Bitmap image)
        {
                int pixelCount = image.Width * image.Height;
                float dpp = pixelCount / (float)(image.Width * image.Height); 
                const float tolerance = 0.01f; 
                return Math.Abs(dpp - 32f) < tolerance;
        }
        public static Bitmap ConvertImageTo32DPI(Bitmap image)
        {
                Bitmap newBitmap = new Bitmap(image.Width, image.Height);

                newBitmap.SetResolution(32, 32);

                using (Graphics graphics = Graphics.FromImage(newBitmap))
                {
                    graphics.DrawImage(image, 0, 0, image.Width, image.Height);
                }

                return newBitmap;
           
        }
        static Bitmap ConvertToGrayscale(Bitmap original)
        {
            Bitmap newBitmap = new Bitmap(original.Width, original.Height);
            using (Graphics g = Graphics.FromImage(newBitmap))
            {
                ColorMatrix colorMatrix = new ColorMatrix(
                   new float[][]
                   {
                      new float[] { 0.3f, 0.3f, 0.3f, 0, 0 },
                      new float[] { 0.59f, 0.59f, 0.59f, 0, 0 },
                      new float[] { 0.11f, 0.11f, 0.11f, 0, 0 },
                      new float[] { 0, 0, 0, 1, 0 },
                      new float[] { 0, 0, 0, 0, 1 }
                   });
                ImageAttributes attributes = new ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);
                g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height), 0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
            }
            return newBitmap;
        }

        static Bitmap BinarizeImage(Bitmap original)
        {
            Bitmap binarized = new Bitmap(original.Width, original.Height);
            for (int y = 0; y < original.Height; y++)
            {
                for (int x = 0; x < original.Width; x++)
                {
                    Color pixelColor = original.GetPixel(x, y);
                    int grayValue = (pixelColor.R + pixelColor.G + pixelColor.B) / 3;
                    binarized.SetPixel(x, y, grayValue < 128 ? Color.Black : Color.White);
                }
            }
            return binarized;
        }

        public Bitmap OtsuBinarization(Bitmap grayscaleImage)
        {
            // Create histogram
            int[] histogram = new int[256];
            for (int y = 0; y < grayscaleImage.Height; y++)
            {
                for (int x = 0; x < grayscaleImage.Width; x++)
                {
                    Color pixel = grayscaleImage.GetPixel(x, y);
                    histogram[pixel.R]++;
                }
            }

            // Total number of pixels
            int totalPixels = grayscaleImage.Width * grayscaleImage.Height;

            // Initialize variables for Otsu's method
            float sum = 0;
            for (int t = 0; t < 256; t++)
            {
                sum += t * histogram[t];
            }

            float sumB = 0;
            int wB = 0;
            int wF = 0;

            float maxVariance = 0;
            int optimalThreshold = 0;

            // Iterate through all possible thresholds
            for (int t = 0; t < 256; t++)
            {
                wB += histogram[t]; // Weight of the background
                if (wB == 0) continue;

                wF = totalPixels - wB; // Weight of the foreground
                if (wF == 0) break;

                sumB += (float)(t * histogram[t]);

                // Background mean
                float mB = sumB / wB;
                // Foreground mean
                float mF = (sum - sumB) / wF;

                // Between-class variance
                float betweenClassVariance = (float)wB * (float)wF * (mB - mF) * (mB - mF);

                // Check if this is the maximum variance
                if (betweenClassVariance > maxVariance)
                {
                    maxVariance = betweenClassVariance;
                    optimalThreshold = t;
                }
            }

            // Create binary image
            Bitmap binaryImage = new Bitmap(grayscaleImage.Width, grayscaleImage.Height);
            for (int y = 0; y < grayscaleImage.Height; y++)
            {
                for (int x = 0; x < grayscaleImage.Width; x++)
                {
                    Color pixel = grayscaleImage.GetPixel(x, y);
                    // Set pixel to black or white based on the optimal threshold
                    binaryImage.SetPixel(x, y, pixel.R < optimalThreshold ? Color.Black : Color.White);
                }
            }

            return binaryImage;
        }
        //public Bitmap ConvertToGrayscale(Bitmap original)
        //{
        //    Bitmap grayImage = new Bitmap(original.Width, original.Height);
        //    for (int y = 0; y < original.Height; y++)
        //    {
        //        for (int x = 0; x < original.Width; x++)
        //        {
        //            Color pixelColor = original.GetPixel(x, y);
        //            int grayValue = (int)(pixelColor.R * 0.3 + pixelColor.G * 0.59 + pixelColor.B * 0.11);
        //            grayImage.SetPixel(x, y, Color.FromArgb(grayValue, grayValue, grayValue));
        //        }
        //    }
        //    return grayImage;
        //}
        private void MoveFiles(List<string> lst)
        {
            if (lst != null)
            {
                foreach (var list in lst)
                {
                    var imglst = list.Split(',');
                    for (int i = 0; i < imglst.Length; i++)
                    {
                        string source = Path.Combine(ConfigurationManager.AppSettings["TempFileSaveLocation"], imglst[i]);
                        string destination = Path.Combine(ConfigurationManager.AppSettings["TempImage"],
                            (i + 1).ToString().PadLeft(4, '0') + ".tif");

                        try
                        {
                            if (!File.Exists(destination))
                            {
                                File.Move(source, destination);
                            }
                        }
                        catch (IOException ioEx)
                        {
                            MessageBox.Show($"Error moving file {imglst[i]}: {ioEx.Message}");
                        }
                    }
                }
                 CommitButtonSeparatorDocument();
            }
        }

        private void CommitButtonSeparatorDocument()
        {
            MessageBox.Show("Files processed and moved successfully!");
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // Automatically fetch files from the directory on form load
            FetchFilesFromDirectory();
        }
    }
}
