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
using Windows.Foundation;

namespace WPFCaptureSample
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
        private string _lastBitmapHash;
        private double lastMouseX;
        private double lastMouseY;

        public MainWindow()
        {
            InitializeComponent();
            InitializeTesseract();
            InitializeCaptureRectangle();
        }

        private void InitializeTesseract()
        {
            try
            {
                _tesseract = new TesseractEngine(TESSDATA_PATH, "eng", EngineMode.Default);
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

                CaptureGridInner.Margin = new Thickness(
                    Math.Max(0, CaptureGridInner.Margin.Left + deltaX), 
                    Math.Max(0, CaptureGridInner.Margin.Top + deltaY), 
                    CaptureGridInner.Margin.Right, 
                    CaptureGridInner.Margin.Bottom
                );
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

                CaptureGridInner.Margin = new Thickness(
                    CaptureGridInner.Margin.Left,
                    CaptureGridInner.Margin.Top,
                    Math.Max(0, CaptureGridInner.Margin.Right + deltaX),
                    Math.Max(0, CaptureGridInner.Margin.Bottom + deltaY)
                );

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
                .Where(p => p.MainWindowHandle != IntPtr.Zero && p.Id != currentProcess.Id)
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

            //var surface = sample.CreateSurface(compositor);
            //imageBrush.Surface = surface;

            sample.StartCapture();
        }

        private Task<string> GetBitmapHash(Bitmap bitmap)
        {
            return Task.Run(() =>
            {
                using (var ms = new MemoryStream())
                {
                    bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                    using (var sha256 = System.Security.Cryptography.SHA256.Create())
                    {
                        return Convert.ToBase64String(sha256.ComputeHash(ms.ToArray()));
                    }
                }
            });

        }

        private async void OnFrameCapturedAsync(object sender, Bitmap bitmap)
        {

            if (_tesseract == null) return;

            // Check if we're already processing
            if (_isProcessingOcr)
            {
                bitmap.Dispose();
                return;
            }

            try
            {
                _isProcessingOcr = true;

                CapturedImage.Source = await Task.Run(() =>
                {
                    // Convert Bitmap to BitmapImage
                    var bitmapImage = new BitmapImage();
                    using (var memoryStream = new MemoryStream())
                    {
                        bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Bmp);
                        memoryStream.Seek(0, SeekOrigin.Begin);

                        bitmapImage.BeginInit();
                        bitmapImage.StreamSource = memoryStream;
                        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                        bitmapImage.EndInit();
                        bitmapImage.Freeze(); // Optional: Freeze to make it cross-thread accessible
                    }
                    return bitmapImage;
                });

                using (bitmap)
                {
                    // Check if the bitmap has changed
                    string currentHash = await GetBitmapHash(bitmap);
                    if (currentHash == _lastBitmapHash)
                    {
                        return;
                    }
                    _lastBitmapHash = currentHash;

                    // Get rectangle properties for OCR
                    var rect = GetRectangleForOCR(bitmap);

                    // Run OCR processing on a background thread
                    var text = await Task.Run(() =>
                    {
                        using (var page = _tesseract.Process(bitmap, rect))
                        {
                            return page.GetText();
                        }
                    });

                    Console.WriteLine("NEWTEXT");
                    await Dispatcher.BeginInvoke(new Action(() =>
                    {
                        OcrResultsTextBox.Text = text;
                        OcrResultsTextBox.ScrollToEnd();
                    }));
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

        public Tesseract.Rect GetRectangleForOCR(Bitmap bitmap)
        {
            double fullWidth = CaptureGrid.ActualWidth;
            double fullHeight = CaptureGrid.ActualHeight;

            double x = CaptureGridInner.Margin.Left;
            double y = CaptureGridInner.Margin.Top;
            double width = CaptureGridInner.ActualWidth;
            double height = CaptureGridInner.ActualHeight;

            return new Tesseract.Rect(
                (int)(bitmap.Width * x / fullWidth), 
                (int)(bitmap.Height * y / fullHeight), 
                (int)(bitmap.Width * width / fullWidth), 
                (int)(bitmap.Height * height / fullHeight)
            );
        }

        private void CapturedImage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            
        }
    }
}
