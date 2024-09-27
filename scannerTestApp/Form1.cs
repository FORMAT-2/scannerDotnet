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

                var Text = ReadText(filePath, filename, bitmap);
                try
                {
                    using (bitmap)
                    {
                        var result = barcodeReader.Decode(bitmap);
                        if (result != null)
                        {
                            if (BarcodeSeparator.Equals(result.Text))
                            {
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

                        strList.Append(filename + ",");
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

                using (var engine = new TesseractEngine(tessDataPath, language, EngineMode.Default))
                {
                    engine.SetVariable("debug_file", "tesseract_log.txt");
                    using (var img = Pix.LoadFromFile(filepath))
                    {
                        using (var processedImage = PreprocessImage(img))
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
            }
            catch(Exception ex){
                throw ex;
            }
            
        }
        public Pix PreprocessImage(Pix img)
        {
            // Convert to grayscale if needed
            var grayImg = img.ConvertRGBToGray(); 

            var binaryImg = grayImg.ConvertTo1Bit(128);

            return binaryImg;
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
        public Bitmap ConvertToGrayscale(Bitmap original)
        {
            Bitmap grayImage = new Bitmap(original.Width, original.Height);
            for (int y = 0; y < original.Height; y++)
            {
                for (int x = 0; x < original.Width; x++)
                {
                    Color pixelColor = original.GetPixel(x, y);
                    int grayValue = (int)(pixelColor.R * 0.3 + pixelColor.G * 0.59 + pixelColor.B * 0.11);
                    grayImage.SetPixel(x, y, Color.FromArgb(grayValue, grayValue, grayValue));
                }
            }
            return grayImage;
        }
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
