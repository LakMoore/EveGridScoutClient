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

using CaptureCore;
using Composition.WindowsRuntimeHelpers;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Windows.Graphics.Capture;
using Tesseract;
using System.IO;
using System.Windows.Media.Imaging;
using System.Collections.Generic;
using System.Xml.Serialization;
using System.Net.Http;
using System.Text;
using System.Runtime.InteropServices;
using Newtonsoft.Json;

namespace GridScout
{

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public System.Drawing.Point ptMinPosition;
        public System.Drawing.Point ptRestorePosition;
        public System.Drawing.Rectangle rcNormalPosition;
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowPlacement(IntPtr hWnd, out WINDOWPLACEMENT lpwndpl);


        private readonly ObservableCollection<Process> processes = new ObservableCollection<Process>();
        private readonly ScoutCaptureCollection _scoutInfo;
        private readonly TesseractEngine _tesseract;
        private EventHandler<IOrderedEnumerable<Process>> ProcessListChanged;

        private const string TESSDATA_PATH = @".\tessdata";
        //private const string SERVER_URL = "https://ffew.space/gridscout/";
        private const string SERVER_URL = "http://localhost:3000/";

        private bool _isProcessingOcr;
        private bool _isDragging;
        private double lastMouseX;
        private double lastMouseY;
        private string itemName = "";
        private Task processChecker;

        public MainWindow()
        {
            InitializeComponent();

            // Initialise Tesseract
            try
            {
                var config = new Dictionary<string, object>
                {
                    { "tessedit_char_blacklist", "@" },
                    { "edges_use_new_outline_complexity", true },
                    //{ "load_system_dawg", false },
                    //{ "load_freq_dawg", false },
                    { "user_patterns_suffix", "user_patterns" },
                    { "user_words_suffix", "user_words" }
                };

                // if we are running from the IDE   
                if (Debugger.IsAttached)
                {
                    config["tessedit_write_images"] = true;
                }

                _tesseract = new TesseractEngine(TESSDATA_PATH, "eve", EngineMode.TesseractOnly, null, config, false);

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing Tesseract OCR: {ex.Message}\nMake sure tessdata folder exists in the application directory.",
                    "OCR Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            InitializeCaptureRectangle();

            // Load persisted values
            if (Properties.Settings.Default.CaptureGrids.Length > 0)
            {
                _scoutInfo = LoadDictionaryFromString(Properties.Settings.Default.CaptureGrids);
            } else
            {
                _scoutInfo = new ScoutCaptureCollection();
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ProcessListChanged += RefreshAvailableEveWindowList;
            // Start the process checker on a separate thread
            processChecker = Task.Run(ProcessChecker);
        }

        public bool IsApplicationMinimized(Process process)
        {
            var handle = process.MainWindowHandle;
            if (handle == IntPtr.Zero)
            {
                return false; // No main window handle, cannot determine minimized state
            }

            GetWindowPlacement(handle, out WINDOWPLACEMENT placement);
            return placement.showCmd == 2; // 2 indicates the window is minimized
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

        private async Task ProcessChecker()
        {
            List<Process> lastProcesses = null;

            while (true)
            {
                try
                {
                    var processesList = Process.GetProcessesByName("exefile")
                        .Where(
                            p =>
                            //p.MainWindowHandle != IntPtr.Zero &&
                            p.MainWindowTitle.StartsWith("Eve -", StringComparison.OrdinalIgnoreCase)
                        )
                        .OrderBy(p => p.MainWindowTitle);

                    // deep comparison of the two lists
                    if (lastProcesses == null || !ProcessListEquals(processesList.ToList(), lastProcesses))
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            ProcessListChanged?.Invoke(this, processesList);
                        });
                    }

                    foreach (var process in processesList)
                    {
                        var info = _scoutInfo.Get(process.MainWindowTitle);
                        if (info != null)
                        {
                            info.IsMinimized = IsApplicationMinimized(process);
                        }
                    }

                    // clone the collection
                    lastProcesses = processesList.ToList();
                } catch (Exception e)
                {
                    Console.WriteLine("Error in ProcessChecker()\n" + e.Message);
                }
                finally
                {
                    await Task.Delay(5000);
                }
            }
        }

        public static bool ProcessListEquals(List<Process> list1, List<Process> list2)
        {   
            // Compare two lists of processes for equality using deep comparison of the process ID
            if (list1 == null || list2 == null || list1.Count != list2.Count)
            {
                return false;
            }

            for (int i = 0; i < list1.Count; i++)
            {
                if (list1[i].Id != list2[i].Id)
                {
                    return false;
                }
            }

            return true;
        }

        private void RefreshAvailableEveWindowList(object sender, IOrderedEnumerable<Process> args)
        {
            var processesList = args;

            // check for missing processes
            foreach (var process in processes)
            {
                var inList = processesList.FirstOrDefault(x => x.MainWindowTitle == process.MainWindowTitle);
                if (inList == null)
                {
                    // process is no longer in the list
                    processes.Remove(process);
                }
            }

            // check for new processes
            foreach (var process in processesList)
            {
                var inList = processes.FirstOrDefault(x => x.MainWindowTitle == process.MainWindowTitle);
                if (inList == null)
                {
                    // totally missing
                    processes.Add(process);
                } 
                else if (inList.Id != process.Id)
                {
                    // present but with a new ID
                    processes.Remove(inList);
                    processes.Add(process);
                }
            }

            // check in-use processes
            foreach (ScoutSelector scout in ScoutSelectorPanel.Children)
            {
                if (scout.SelectedProcess != null)
                {
                    var inList = processes.FirstOrDefault(
                        x => x.MainWindowTitle == scout.SelectedProcess.MainWindowTitle
                    );
                    if (inList != null)
                    {
                        if (inList.Id != scout.SelectedProcess.Id)
                        {
                            // We've found a new process with a matching title!!
                            var info = _scoutInfo.Get(inList.MainWindowTitle);
                            var capture = info.Capture;

                            // Stop the old capture
                            if (capture != null)
                            {
                                capture.FrameCaptured -= OnFrameCapturedAsync;
                                capture.StopCapture();
                                capture.Dispose();
                                capture = null;
                            }
                            info.Capture = null;

                            // Start a new capture
                            StartCaptureFromProcess(inList, capture.Wormhole);

                            // update the process stored in the scout object
                            scout.TryGetNewVersionOfProcess();

                            Console.WriteLine("Switched to new process for " + info.Key);
                        }
                        processes.Remove(inList);
                    }
                }
            }
        }

        private void StartHwndCapture(IntPtr hwnd, string wormhole)
        {
            var item = CaptureHelper.CreateItemForWindow(hwnd);
            if (item != null)
            {
                StartCaptureFromItem(item, wormhole);
            }
        }

        private void StartCaptureFromItem(GraphicsCaptureItem item, string wormhole)
        {
            if (item == null)
            {
                return;
            }

            var device = Direct3D11Helper.CreateDevice();
            var capture = new BasicCapture(device, item)
            {
                Wormhole = wormhole
            };
            capture.FrameCaptured += OnFrameCapturedAsync;
            capture.StartCapture();

            itemName = item.DisplayName;

            if (!_scoutInfo.ContainsKey(itemName))
            {
                var thisSC = new ScoutCapture
                {
                    Key = itemName,
                    Margins = new Thickness()
                };
                _scoutInfo.Add(thisSC);
            }

            var scoutCapture = _scoutInfo.Get(itemName);
            scoutCapture.Capture = capture;

            var rect = scoutCapture.Margins;
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

            BasicCapture src = (BasicCapture)sender;
            var thisItemName = src.GetItem().DisplayName;
            var thisScout = _scoutInfo.Get(thisItemName);

            try
            {
                _isProcessingOcr = true;

                float scale = float.Parse(ValueOne.Text);
                int whSize = int.Parse(ValueThree.Text);
                float factor = float.Parse(ValueFour.Text);

                using (bitmap)
                {
                    src.PauseCapture();

                    var margins = thisScout.Margins;

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
                            tempPix = tempPix.ConvertRGBToGray();
                            tempPix = tempPix.Invert();   // This is essential
                            tempPix = tempPix.Scale(scale, scale);  // 5f is good
                            tempPix = tempPix.BinarizeSauvola(whSize, factor, false);  //10, 0.1, false works with scale = 5f
                        }
                        return (tempBitmap, tempPix);
                    });

                    thisScout.LastImage = bitmapImage;

                    if (thisItemName == itemName)
                    {
                        CapturedImage.Source = bitmapImage;
                    }

                    // Check if the bitmap has changed
                    if (!pix.Equals(thisScout.LastPix))
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

                        var message = new ScoutMessage
                        {
                            Message = text,
                            Scout = thisScout.Key.Substring(6),
                            Wormhole = src.Wormhole
                        };

                        var json = JsonConvert.SerializeObject(message);

                        using (var client = new HttpClient())
                        {
                            var content = new StringContent(json, Encoding.UTF8, "application/json");
                            var response = await client.PostAsync(SERVER_URL, content);
                            var body = await response.Content.ReadAsStringAsync();
                            Console.WriteLine(body);
                        }

                    }
                    thisScout.LastPix = pix.Clone();
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
                var toResume = await _scoutInfo.GetNextInOrder(thisScout);
                toResume.Capture.ResumeCapture();
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
                scout.Margins = new Thickness(
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

            UnSelectOthers(scout);

            var process = scout.SelectedProcess;

            if (process != null)
            {
                StartCaptureFromProcess(process, scout.ScoutLabelContent);

                // remove process from the list so it cannot be used by other selectors
                processes.Remove(process);
            }
        }

        private void StartCaptureFromProcess(Process process, string wormhole)
        {
            var hwnd = process.MainWindowHandle;
            try
            {
                StartHwndCapture(hwnd, wormhole);
            }
            catch (Exception)
            {
                Debug.WriteLine($"Hwnd 0x{hwnd.ToInt32():X8} is not valid for capture!");
            }
        }

        private void UnSelectOthers(ScoutSelector scout)
        {
            foreach (ScoutSelector item in ScoutSelectorPanel.Children)
            {
                if (item != scout)
                {
                    item.NotSelected();
                }
            }
        }

        private void ScoutSelector_ShowScout(object sender, EventArgs e)
        {
            var scout = (ScoutSelector)sender;
            itemName = scout.ScoutLabelContent;
            
            UnSelectOthers(scout);

            Process process = scout.SelectedProcess;
            itemName = process.MainWindowTitle;

            var info = _scoutInfo.Get(itemName);
            var margins = info.Margins;
            LeftTextBox.Value = (int)margins.Left;
            TopTextBox.Value = (int)margins.Top;
            BottomTextBox.Value = (int)margins.Bottom;
            RightTextBox.Value = (int)margins.Right;

            CapturedImage.Source = info.LastImage;
            ReDrawCaptureRectangle();
        }

        private void ScoutSelector_StopScout(object sender, EventArgs e)
        {
            var scout = (ScoutSelector)sender;
            Process process = scout.SelectedProcess;

            itemName = process.MainWindowTitle;

            UnSelectOthers(scout);

            var capture = _scoutInfo.Get(itemName).Capture;
            if (capture != null)
            {
                capture.FrameCaptured -= OnFrameCapturedAsync;
                capture.StopCapture();
                capture.Dispose();
                capture = null;
                _scoutInfo.Get(itemName).Capture = null;
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
    }
}
