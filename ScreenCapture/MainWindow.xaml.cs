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
using System.Numerics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Windows.Graphics.Capture;
using Windows.UI.Composition;
using Tesseract;
using System.IO;

namespace WPFCaptureSample
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private IntPtr hwnd;
        private Compositor compositor;
        private Windows.UI.Composition.CompositionTarget target;
        private Windows.UI.Composition.ContainerVisual root;
        private SpriteVisual visual;
        private CompositionSurfaceBrush imageBrush;
        private BasicCapture sample;
        private ObservableCollection<Process> processes;
        private ObservableCollection<MonitorInfo> monitors;
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

            CaptureGridInner.Width = CaptureGrid.Width;
            CaptureGridInner.Height = CaptureGrid.Height;

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
            MonitorComboBox.SelectedIndex = -1;
            await StartPickerCaptureAsync();
        }

        private void PrimaryMonitorButton_Click(object sender, RoutedEventArgs e)
        {
            StopCapture();
            WindowComboBox.SelectedIndex = -1;
            MonitorComboBox.SelectedIndex = -1;
            StartPrimaryMonitorCapture();
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

            InitComposition(controlsWidth);
            InitWindowList();
            InitMonitorList();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopCapture();
            WindowComboBox.SelectedIndex = -1;
            MonitorComboBox.SelectedIndex = -1;
        }

        private void WindowComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = (ComboBox)sender;
            var process = (Process)comboBox.SelectedItem;

            if (process != null)
            {
                StopCapture();
                MonitorComboBox.SelectedIndex = -1;
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

        private void MonitorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = (ComboBox)sender;
            var monitor = (MonitorInfo)comboBox.SelectedItem;

            if (monitor != null)
            {
                StopCapture();
                WindowComboBox.SelectedIndex = -1;
                var hmon = monitor.Hmon;
                try
                {
                    StartHmonCapture(hmon);
                }
                catch (Exception)
                {
                    Debug.WriteLine($"Hmon 0x{hmon.ToInt32():X8} is not valid for capture!");
                    monitors.Remove(monitor);
                    comboBox.SelectedIndex = -1;
                }
            }
        }

        private void InitComposition(float controlsWidth)
        {
            compositor = new Compositor();
            target = compositor.CreateDesktopWindowTarget(hwnd, true);

            root = compositor.CreateContainerVisual();
            root.RelativeSizeAdjustment = Vector2.One;
            root.Size = new Vector2(-controlsWidth, 0);
            root.Offset = new Vector3(controlsWidth, 0, 0);
            target.Root = root;

            visual = compositor.CreateSpriteVisual();
            visual.RelativeSizeAdjustment = Vector2.One;

            imageBrush = compositor.CreateSurfaceBrush();
            imageBrush.Stretch = CompositionStretch.Uniform;
            visual.Brush = imageBrush;

            root.Children.InsertAtTop(visual);
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

        private void InitMonitorList()
        {
            monitors = new ObservableCollection<MonitorInfo>();
            MonitorComboBox.ItemsSource = monitors;

            var allMonitors = MonitorEnumerationHelper.GetMonitors();
            foreach (var monitor in allMonitors)
            {
                monitors.Add(monitor);
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

        private void StartHmonCapture(IntPtr hmon)
        {
            var item = CaptureHelper.CreateItemForMonitor(hmon);
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

        private void StartPrimaryMonitorCapture()
        {
            var monitor = MonitorEnumerationHelper.GetMonitors().First();
            StartHmonCapture(monitor.Hmon);
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

            var surface = sample.CreateSurface(compositor);
            imageBrush.Surface = surface;

            sample.StartCapture();
        }

        private string GetBitmapHash(Bitmap bitmap)
        {
            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    return Convert.ToBase64String(sha256.ComputeHash(ms.ToArray()));
                }
            }
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
                using (bitmap)
                {
                    // Check if the bitmap has changed
                    string currentHash = GetBitmapHash(bitmap);
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
    }
}
