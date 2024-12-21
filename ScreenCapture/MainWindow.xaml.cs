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
        private ObservableCollection<Process> processes = new ObservableCollection<Process>();
        private TesseractEngine _tesseract;
        private const string TESSDATA_PATH = @"./tessdata";
        private bool _isProcessingOcr;
        private bool _isDragging;
        private double lastMouseX;
        private double lastMouseY;
        private string itemName = "";
        private ScoutCaptureCollection _scoutInfo;

        public MainWindow()
        {
            InitializeComponent();
            InitializeTesseract();
            InitializeCaptureRectangle();

            // Load persisted values
            if (Properties.Settings.Default.CaptureGrids.Length > 0)
            {
                _scoutInfo = LoadDictionaryFromString(Properties.Settings.Default.CaptureGrids);
            }
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

            RefreshAvailableEveWindowList();
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

        private void RefreshAvailableEveWindowList()
        {
            var processesList = Process.GetProcesses()
                    .Where(
                        p => p.MainWindowHandle != IntPtr.Zero
                        && p.MainWindowTitle.StartsWith("Eve -", StringComparison.OrdinalIgnoreCase)
                    )
                    .OrderBy(p => p.MainWindowTitle);

            foreach (var process in processesList)
            {
                if (processes.FirstOrDefault(x => x.Id == process.Id) == null)
                {
                    processes.Add(process);
                }
            }

            foreach (ScoutSelector scout in ScoutSelectorPanel.Children)
            {
                if (scout.SelectedProcess != null)
                {
                    var matchingProcesses = processes.Where(x => x.Id == scout.SelectedProcess.Id).ToList();
                    foreach (Process processToRemove in matchingProcesses)
                    {
                        processes.Remove(processToRemove);
                    }
                }
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

        private void StartCaptureFromItem(GraphicsCaptureItem item)
        {
            if (item == null)
            {
                return;
            }

            var device = Direct3D11Helper.CreateDevice();
            var capture = new BasicCapture(device, item);
            capture.FrameCaptured += OnFrameCapturedAsync;
            capture.StartCapture();

            itemName = item.DisplayName;

            if (!_scoutInfo.ContainsKey(itemName))
            {
                var thisSC = new ScoutCapture
                {
                    Key = itemName,
                    margins = new Thickness()
                };
                _scoutInfo.Add(thisSC);
            }

            var scoutCapture = _scoutInfo.Get(itemName);
            scoutCapture.capture = capture;

            var rect = scoutCapture.margins;
            TopTextBox.Value = (int)rect.Top;
            LeftTextBox.Value = (int)rect.Left;
            RightTextBox.Value = (int)rect.Right;
            BottomTextBox.Value = (int)rect.Bottom;

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

                using (bitmap)
                {
                    BasicCapture src = (BasicCapture)sender;
                    var thisItemName = src.GetItem().DisplayName;
                    var thisScout = _scoutInfo.Get(thisItemName);

                    src.PauseCapture();
                    var toResume = _scoutInfo.GetNextInOrder(thisScout);
                    toResume.capture.ResumeCapture();

                    var margins = thisScout.margins;

                    var cropRect = new Rectangle(
                        (int)margins.Left,
                        (int)margins.Top,
                        bitmap.Width - (int)(margins.Right + margins.Left),
                        bitmap.Height - (int)(margins.Bottom + margins.Top)
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
                            tempPix = tempPix.BinarizeSauvola(9, 0.185f, false);  //9, 0.185f, false works with scale = 5f
                            //tempPix = tempPix.BinarizeSauvola(valueOne, valueTwo, false);  //8, 0.19f, false works
                        }
                        return (tempBitmap, tempPix);
                    });

                    thisScout.lastImage = bitmapImage;

                    if (thisItemName == itemName)
                    {
                        CapturedImage.Source = bitmapImage;
                    }

                    // Check if the bitmap has changed
                    if (!pix.Equals(thisScout.lastPix))
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
                    thisScout.lastPix = pix.Clone();
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

                var scout = _scoutInfo.Get(itemName);
                scout.margins = new Thickness(
                    (int)LeftTextBox.Value,
                    (int)TopTextBox.Value,
                    (int)RightTextBox.Value,
                    (int)BottomTextBox.Value
                );

                if (!_isDragging)
                {
                    SaveDetails();
                }

            }
        }

        private void SaveDetails()
        {
            // Save the captureGrids dictionary to a string
            Properties.Settings.Default.CaptureGrids = SaveDictionaryToXml(_scoutInfo);
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
        public string SaveDictionaryToXml(ScoutCaptureCollection serializableDictionary)
        {
            var serializer = new XmlSerializer(typeof(ScoutCaptureCollection));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, serializableDictionary);
                return writer.ToString();
            }
        }

        public ScoutCaptureCollection LoadDictionaryFromString(string xmlString)
        {
            var serializer = new XmlSerializer(typeof(ScoutCaptureCollection));
            using (var reader = new StringReader(xmlString))
            {
                var serializableDictionary = (ScoutCaptureCollection)serializer.Deserialize(reader);
                return serializableDictionary;
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            // ScoutSelectorPanel's children follow the patterh #A, #B, #C, #D, #E, etc.
            // Find the first gap in the pattern and create a new ScoutSelector there
            int i = 0;
            for (i = 0; i < ScoutSelectorPanel.Children.Count; i++)
            {
                var scout = (ScoutSelector)ScoutSelectorPanel.Children[i];
                if (scout.ScoutLabelContent != "#" + ((char)('A' + i)).ToString())
                {
                    break;
                }
            }

            var newScout = new ScoutSelector
            {
                ScoutLabelContent = "#" + ((char)('A' + i)).ToString()
            };
            newScout.ScoutSelected += ScoutSelector_ScoutSelected;
            newScout.ShowScout += ScoutSelector_ShowScout;
            newScout.StopScout += ScoutSelector_StopScout;
            newScout.DeleteScout += ScoutSelector_DeleteScout;
            newScout.SetProcesses(processes);
            ScoutSelectorPanel.Children.Insert(i, newScout);
        }

        private void ScoutSelector_DeleteScout(object sender, EventArgs e)
        {
            var scout = (ScoutSelector)sender;
            ScoutSelectorPanel.Children.Remove(scout);
        }

        private void ScoutSelector_ScoutSelected(object sender, EventArgs e)
        {
            var scout = (ScoutSelector)sender;
            itemName = scout.ScoutLabelContent;
            foreach (ScoutSelector item in ScoutSelectorPanel.Children)
            {
                if (item != scout)
                {
                    item.NotSelected();
                }
            }

            var process = scout.SelectedProcess;

            if (process != null)
            {
                var hwnd = process.MainWindowHandle;
                try
                {
                    StartHwndCapture(hwnd);
                }
                catch (Exception)
                {
                    Debug.WriteLine($"Hwnd 0x{hwnd.ToInt32():X8} is not valid for capture!");
                    scout.ClearSelection();
                }

                // remove process from the list so it cannot be used by other selectors
                processes.Remove(process);
            }
        }

        private void ScoutSelector_ShowScout(object sender, EventArgs e)
        {
            var scout = (ScoutSelector)sender;
            itemName = scout.ScoutLabelContent;
            foreach (ScoutSelector item in ScoutSelectorPanel.Children)
            {
                if (item != scout)
                {
                    item.NotSelected();
                }
            }

            Process process = scout.SelectedProcess;
            itemName = process.MainWindowTitle;

            var info = _scoutInfo.Get(itemName);
            var margins = info.margins;
            LeftTextBox.Value = (int)margins.Left;
            TopTextBox.Value = (int)margins.Top;
            BottomTextBox.Value = (int)margins.Bottom;
            RightTextBox.Value = (int)margins.Right;

            CapturedImage.Source = info.lastImage;
            ReDrawCaptureRectangle();
        }

        private void ScoutSelector_StopScout(object sender, EventArgs e)
        {
            var scout = (ScoutSelector)sender;
            Process process = scout.SelectedProcess;

            itemName = process.MainWindowTitle;

            foreach (ScoutSelector item in ScoutSelectorPanel.Children)
            {
                if (item != scout)
                {
                    item.NotSelected();
                }
            }

            var capture = _scoutInfo.Get(itemName).capture;
            if (capture != null)
            {
                capture.FrameCaptured -= OnFrameCapturedAsync;
                capture.StopCapture();
                capture.Dispose();
                capture = null;
                _scoutInfo.Get(itemName).capture = null;
            }

            // Add the process back into the list of available processes
            processes.Add(process);

            CapturedImage.Source = null;
            itemName = "";
            TopTextBox.Value = 0;
            RightTextBox.Value = 0;
            BottomTextBox.Value = 0;
            LeftTextBox.Value = 0;
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshAvailableEveWindowList();
        }
    }
}
