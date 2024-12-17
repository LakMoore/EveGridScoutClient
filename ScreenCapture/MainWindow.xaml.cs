//  ---------------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// 
//  The MIT License (MIT)
// 
//  Permission is hereby granted, free of charge, to any person obtaining a copy
//  of this software and associated documentation files (the "Software"), to deal
//  in the Software without restriction, including without limitation the rights
//  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//  copies of the Software, and to permit persons to whom the Software is
//  furnished to do so, subject to the following conditions:
// 
//  The above copyright notice and this permission notice shall be included in
//  all copies or substantial portions of the Software.
// 
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//  THE SOFTWARE.
//  ---------------------------------------------------------------------------------

using CaptureSampleCore;
using Composition.WindowsRuntimeHelpers;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Windows.Graphics.Capture;
using Tesseract;
using System.IO;
using System.Windows.Media.Imaging;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace GridScout
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private IntPtr hwnd;
        private BasicCapture sample;
        private ObservableCollection<Process> processes;
        private TesseractEngine _tesseract;
        private const string TESSDATA_PATH = @"./tessdata";
        private bool _isProcessingOcr;
        private bool _isDragging;
        private Pix _lastPix;
        private double lastMouseX;
        private double lastMouseY;
        private string itemName = "";
        private Dictionary<string, Thickness> captureGrids = new Dictionary<string, Thickness>();

        public MainWindow()
        {
            InitializeComponent();
            InitializeTesseract();
            InitializeCaptureRectangle();

            // Load persisted values
            if (Properties.Settings.Default.CaptureGrids.Length > 0)
            {
                captureGrids = LoadDictionaryFromString(Properties.Settings.Default.CaptureGrids);
            }
        }

        private void InitializeTesseract()
        {
            try
            {
                //tessedit_write_images
                var config = new Dictionary<string, object>();
                config.Add("tessedit_write_images", true);
                //config.Add("load_system_dawg", false);
                //config.Add("load_freq_dawg", false);

                _tesseract = new TesseractEngine(TESSDATA_PATH, "eng", EngineMode.LstmOnly, null, config, false);

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing Tesseract OCR: {ex.Message}\nMake sure tessdata folder exists in the application directory.",
                    "OCR Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializeCaptureRectangle()
        {
            // Initialise to parent's size
            CaptureGridInner.Width = CaptureGrid.ActualWidth;
            CaptureGridInner.Height = CaptureGrid.ActualHeight;

            // Mouse events for resizing
            dragTopLeft.MouseMove += DragTopLeft_MouseMove;
            dragTopLeft.MouseLeftButtonDown += DragHandle_MouseLeftButtonDown;
            dragTopLeft.MouseLeftButtonUp += DragHandle_MouseLeftButtonUp;
            dragBottomRight.MouseMove += DragBottomRight_MouseMove;
            dragBottomRight.MouseLeftButtonDown += DragHandle_MouseLeftButtonDown;
            dragBottomRight.MouseLeftButtonUp += DragHandle_MouseLeftButtonUp;
        }

        private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Logic to initiate resizing
            if (sender is System.Windows.Shapes.Rectangle rectangle)
            {
                _isDragging = true;
                rectangle.CaptureMouse();
                lastMouseX = e.GetPosition(myCanvas).X;
                lastMouseY = e.GetPosition(myCanvas).Y;
            }
        }

        private void DragTopLeft_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is System.Windows.Shapes.Rectangle rectangle && rectangle.IsMouseCaptured)
            {
                double mouseX = e.GetPosition(myCanvas).X;
                double mouseY = e.GetPosition(myCanvas).Y;

                double deltaX = mouseX - lastMouseX;
                double deltaY = mouseY - lastMouseY;

                double speed = CapturedImage.Source.Height / CapturedImage.ActualHeight;

                LeftTextBox.Value += (int)(deltaX * speed);
                TopTextBox.Value += (int)(deltaY * speed);

                lastMouseX = mouseX;
                lastMouseY = mouseY;
            }
        }

        private void DragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Release mouse capture
            if (sender is System.Windows.Shapes.Rectangle rectangle)
            {
                rectangle.ReleaseMouseCapture();
                _isDragging = false;
                SaveDetails();
            }

        }

        private void DragBottomRight_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is System.Windows.Shapes.Rectangle rectangle && rectangle.IsMouseCaptured)
            {

                double mouseX = e.GetPosition(myCanvas).X;
                double mouseY = e.GetPosition(myCanvas).Y;

                double deltaX = lastMouseX - mouseX;
                double deltaY = lastMouseY - mouseY;

                double speed = CapturedImage.Source.Height / CapturedImage.ActualHeight;

                RightTextBox.Value += (int)(deltaX * speed);
                BottomTextBox.Value += (int)(deltaY * speed);

                lastMouseX = mouseX;
                lastMouseY = mouseY;
            }
        }

        private async void PickerButton_Click(object sender, RoutedEventArgs e)
        {
            StopCapture();
            WindowComboBox.SelectedIndex = -1;
            await StartPickerCaptureAsync();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var interopWindow = new WindowInteropHelper(this);
            hwnd = interopWindow.Handle;

            var presentationSource = PresentationSource.FromVisual(this);
            double dpiX = 1.0;
            double dpiY = 1.0;
            if (presentationSource != null)
            {
                dpiX = presentationSource.CompositionTarget.TransformToDevice.M11;
                dpiY = presentationSource.CompositionTarget.TransformToDevice.M22;
            }
            var controlsWidth = (float)(ControlsGrid.ActualWidth * dpiX);

            InitWindowList();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopCapture();
            WindowComboBox.SelectedIndex = -1;
        }

        private void WindowComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = (ComboBox)sender;
            var process = (Process)comboBox.SelectedItem;

            if (process != null)
            {
                StopCapture();
                var hwnd = process.MainWindowHandle;
                try
                {
                    StartHwndCapture(hwnd);
                }
                catch (Exception)
                {
                    Debug.WriteLine($"Hwnd 0x{hwnd.ToInt32():X8} is not valid for capture!");
                    processes.Remove(process);
                    comboBox.SelectedIndex = -1;
                }
            }
        }

        private void InitWindowList()
        {
            processes = new ObservableCollection<Process>();
            WindowComboBox.ItemsSource = processes;

            var currentProcess = Process.GetCurrentProcess();
            var processesList = Process.GetProcesses()
                .Where(
                    p => p.MainWindowHandle != IntPtr.Zero
                    && p.Id != currentProcess.Id
                    && p.MainWindowTitle.StartsWith("Eve", StringComparison.OrdinalIgnoreCase)
                )
                .OrderBy(p => p.MainWindowTitle);
            foreach (var process in processesList)
            {
                processes.Add(process);
            }
        }
        private async Task StartPickerCaptureAsync()
        {
            var picker = new GraphicsCapturePicker();
            picker.SetWindow(new WindowInteropHelper(this).Handle);
            var item = await picker.PickSingleItemAsync();
            if (item != null)
            {
                StartCaptureFromItem(item);
            }
        }

        private void StartHwndCapture(IntPtr hwnd)
        {
            var item = CaptureHelper.CreateItemForWindow(hwnd);
            if (item != null)
            {
                StartCaptureFromItem(item);
            }
        }

        private void StopCapture()
        {
            if (sample != null)
            {
                sample.FrameCaptured -= OnFrameCapturedAsync;
                sample.StopCapture();
                sample.Dispose();
                sample = null;
            }
        }

        private void StartCaptureFromItem(GraphicsCaptureItem item)
        {
            if (item == null)
            {
                return;
            }

            StopCapture();

            var device = Direct3D11Helper.CreateDevice();
            sample = new BasicCapture(device, item);
            sample.FrameCaptured += OnFrameCapturedAsync;
            sample.StartCapture();

            itemName = item.DisplayName;

            // Fetch the rect from captureGrids using the item title
            if (captureGrids.TryGetValue(item.DisplayName, out var rect))
            {
                TopTextBox.Value = (int)rect.Top;
                LeftTextBox.Value = (int)rect.Left;
                RightTextBox.Value = (int)rect.Right;
                BottomTextBox.Value = (int)rect.Bottom;
            }

        }

        private async void OnFrameCapturedAsync(object sender, Bitmap bitmap)
        {

            if (_tesseract == null) return;

            // Check if we're already processing
            if (_isProcessingOcr || _isDragging)
            {
                bitmap.Dispose();
                return;
            }

            try
            {
                _isProcessingOcr = true;

                float scale = 5f;
                int valueOne = int.Parse(ValueOne.Text);
                if (valueOne == 0) valueOne = 2;
                float valueTwo = float.Parse(ValueTwo.Text);
                if (float.IsNaN(valueTwo)) valueTwo = 0.1f;

                using (bitmap)
                {
                    var cropRect = new Rectangle(
                        (int)LeftTextBox.Value,
                        (int)TopTextBox.Value,
                        bitmap.Width - (int)RightTextBox.Value - (int)LeftTextBox.Value,
                        bitmap.Height - (int)BottomTextBox.Value - (int)TopTextBox.Value
                    );

                    (var bitmapImage, var pix) = await Task.Run(() =>
                    {
                        // Convert Bitmap to BitmapImage
                        var tempBitmap = new BitmapImage();
                        using (var memoryStream = new MemoryStream())
                        {
                            bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Bmp);
                            memoryStream.Seek(0, SeekOrigin.Begin);

                            tempBitmap.BeginInit();
                            tempBitmap.StreamSource = memoryStream;
                            tempBitmap.CacheOption = BitmapCacheOption.OnLoad;
                            tempBitmap.EndInit();
                            tempBitmap.Freeze(); // Optional: Freeze to make it cross-thread accessible
                        }

                        Pix tempPix;
                        using (var memoryStream = new MemoryStream())
                        {
                            using (var croppedBitmap = new Bitmap(cropRect.Width, cropRect.Height))
                            {
                                using (var g = Graphics.FromImage(croppedBitmap))
                                {
                                    g.DrawImage(bitmap, new Rectangle(0, 0, croppedBitmap.Width, croppedBitmap.Height), cropRect, GraphicsUnit.Pixel);
                                }
                                croppedBitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Bmp);
                            }
                            memoryStream.Seek(0, SeekOrigin.Begin);

                            tempPix = Pix.LoadFromMemory(memoryStream.ToArray());
                            tempPix = tempPix.Scale(scale, scale);
                            tempPix = tempPix.ConvertRGBToGray();
                            tempPix = tempPix.Invert();   // This is essential
                            tempPix = tempPix.BinarizeSauvola(8, 0.19f, false);  //8, 0.19f, false works with scale = 5f
                                                                                 //tempPix = tempPix.BinarizeSauvola(valueOne, valueTwo, false);  //8, 0.19f, false works
                        }
                        return (tempBitmap, tempPix);
                    });

                    CapturedImage.Source = bitmapImage;

                    // Check if the bitmap has changed
                    if (!pix.Equals(_lastPix))
                    {
                        // Run OCR processing on a background thread
                        var text = await Task.Run(() =>
                        {
                            using (var page = _tesseract.Process(
                                pix,
                                PageSegMode.SingleBlock
                            ))
                            {
                                return page.GetText();
                            }
                        });

                        await Dispatcher.BeginInvoke(new Action(() =>
                        {
                            OcrResultsTextBox.Text = text;
                            OcrResultsTextBox.ScrollToEnd();
                        }));
                    }
                    _lastPix = pix.Clone();
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.BeginInvoke(new Action(() =>
                {
                    OcrResultsTextBox.AppendText($"OCR Error: {ex.Message}{Environment.NewLine}");
                }));
            }
            finally
            {
                _isProcessingOcr = false;
            }
        }

        private void CapturedImage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ReDrawCaptureRectangle();
        }

        private void MarginValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (CapturedImage != null && CapturedImage.Source != null && !double.IsNaN(CapturedImage.ActualHeight))
            {
                ReDrawCaptureRectangle();

                // Save the current values to captupeGrids
                if (!captureGrids.ContainsKey(itemName))
                {
                    captureGrids.Add(itemName, new Thickness(
                        (int)LeftTextBox.Value,
                        (int)TopTextBox.Value,
                        (int)RightTextBox.Value,
                        (int)BottomTextBox.Value
                    ));
                }
                else
                {
                    captureGrids[itemName] = new Thickness(
                        (int)LeftTextBox.Value,
                        (int)TopTextBox.Value,
                        (int)RightTextBox.Value,
                        (int)BottomTextBox.Value
                    );
                }

                if (!_isDragging)
                {
                    SaveDetails();
                }

            }
        }

        private void SaveDetails()
        {
            // Save the captureGrids dictionary to a string
            Properties.Settings.Default.CaptureGrids = SaveDictionaryToXml(captureGrids);
            Properties.Settings.Default.Save(); // Persist the changes
        }

        private void ReDrawCaptureRectangle()
        {
            if (CapturedImage != null && CapturedImage.Source != null && !double.IsNaN(CapturedImage.ActualHeight))
            {
                double imageW = CapturedImage.Source.Width;
                double imageH = CapturedImage.Source.Height;

                double w = CapturedImage.ActualWidth;
                double h = CapturedImage.ActualHeight;

                double left = (double)(w * LeftTextBox.Value / imageW);
                double top = (double)(h * TopTextBox.Value / imageH);
                double right = (double)(w * RightTextBox.Value / imageW);
                double bottom = (double)(h * BottomTextBox.Value / imageH);

                double heightPadding = Math.Max(0, (CaptureGrid.ActualHeight - h) / 2);
                double widthPadding = Math.Max(0,  (CaptureGrid.ActualWidth - w) / 2);

                CaptureGridInner.Width = Math.Max(40, w - right - left);
                CaptureGridInner.Height = Math.Max(40, h - top - bottom);

                CaptureGridInner.Margin = new Thickness(
                    widthPadding + left,
                    heightPadding + top,
                    widthPadding + right,
                    heightPadding + bottom
                );

            }
        }
        public string SaveDictionaryToXml(Dictionary<string, Thickness> dictionary)
        {
            var serializableDictionary = new SerializableDictionary();

            foreach (var kvp in dictionary)
            {
                serializableDictionary.Entries.Add(new DictionaryEntry
                {
                    Key = kvp.Key,
                    Value = kvp.Value
                });
            }

            var serializer = new XmlSerializer(typeof(SerializableDictionary));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, serializableDictionary);
                return writer.ToString();
            }
        }

        public Dictionary<string, Thickness> LoadDictionaryFromString(string xmlString)
        {
            var serializer = new XmlSerializer(typeof(SerializableDictionary));
            using (var reader = new StringReader(xmlString))
            {
                var serializableDictionary = (SerializableDictionary)serializer.Deserialize(reader);
                var dictionary = new Dictionary<string, Thickness>();

                foreach (var entry in serializableDictionary.Entries)
                {
                    dictionary[entry.Key] = entry.Value;
                }

                return dictionary;
            }
        }
    }
}
