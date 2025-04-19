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
using NAudio.Wave;
using NAudio.SoundFont;

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
        private readonly string SERVER_URL;
        private EventHandler<List<Process>> ProcessListChanged;
        private EventHandler<Process> MinimisedStateChanged;

        private const string TESSDATA_PATH = @".\tessdata";
        private const long KEEP_ALIVE_INTERVAL = 5 * TimeSpan.TicksPerMinute; // 5 minutes in ticks

        private bool _isCapturingImage;
        private bool _isDragging;
        private double lastMouseX;
        private double lastMouseY;
        private string itemName = "";
        private Task processChecker;
        private const float NOISE_THRESHOLD = 0.1f; // Adjust this value to change sensitivity
        private event EventHandler<string> LoudNoiseDetected;

        public MainWindow()
        {
            InitializeComponent();

            this.Title = $"GridScout Client v{GetVersion()}";

            LoudNoiseDetected += async (sender, wormhole) =>
            {
                var src = (sender as GraphicsCaptureItem);
                if (src != null)
                {
                    var message = new ScoutMessage
                    {
                        Message = $"Possible activation detected!",
                        Scout = src.DisplayName.Substring(6),
                        Wormhole = wormhole
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
            };

            // Initialise Tesseract
            try
            {
                var config = new Dictionary<string, object>
                {
                    { "tessedit_char_blacklist", "@|" },
                    { "edges_use_new_outline_complexity", true },
                    //{ "load_system_dawg", false },
                    //{ "load_freq_dawg", false },
                    { "user_defined_dpi", "288" },  //x3 scale
                    // { "user_defined_dpi", "384" },   //x4 scale
                    { "user_patterns_suffix", "user_patterns" },
                    { "user_words_suffix", "user_words" }
                };

                // if we are running from the IDE   
                if (Debugger.IsAttached)
                {
                    config["tessedit_write_images"] = true;
                    // SERVER_URL = "https://ffew.space/gridscout/";
                    SERVER_URL = "http://localhost:3000/";
                }
                else
                {
                    SERVER_URL = "https://ffew.space/gridscout/";
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

        public string GetVersion()
        {
            if (System.Deployment.Application.ApplicationDeployment.IsNetworkDeployed)
            {
                return System.Deployment.Application.ApplicationDeployment.CurrentDeployment.CurrentVersion.ToString();
            }

            return "1.0.0.DEV";
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ProcessListChanged += RefreshAvailableEveWindowList;
            MinimisedStateChanged += UpdateMinimizedState;

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
                        .OrderBy(p => p.MainWindowTitle).ToList();

                    // deep comparison of the two lists
                    if (lastProcesses == null || !ProcessListEquals(processesList, lastProcesses))
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
                            var wasMinimized = info.IsMinimized;
                            info.IsMinimized = IsApplicationMinimized(process);
                            if (wasMinimized != info.IsMinimized)
                            {
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    MinimisedStateChanged?.Invoke(this, process);
                                });
                            }
                        }
                    }

                    // clone the collection
                    lastProcesses = new List<Process>();
                    foreach (var process in processesList)
                    {
                        lastProcesses.Add(process);
                    }
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

        private void UpdateMinimizedState(object sender, Process args)
        {
            foreach(ScoutSelector scout in ScoutSelectorPanel.Children)
            {
                if (scout.SelectedProcess?.Id == args.Id)
                {
                    var info = _scoutInfo.Get(args.MainWindowTitle);
                    if (info != null)
                    {
                        scout.SetMinimised(info.IsMinimized);
                    }
                }
            }

        }

        private void RefreshAvailableEveWindowList(object sender, List<Process> args)
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
                    var info = _scoutInfo.Get(scout.SelectedProcess.MainWindowTitle);
                    var capture = info.Capture;

                    var inList = processes.FirstOrDefault(
                        x => x.MainWindowTitle == scout.SelectedProcess.MainWindowTitle
                    );
                    if (inList != null)
                    {
                        if (inList.Id != scout.SelectedProcess.Id)
                        {
                            // We've found a new process with a matching title!!

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
                            StartCaptureFromProcess(inList, scout.ScoutLabelContent);

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

            // fetch or create the ScoutInfo entry
            var thisSC = _scoutInfo.GetOrDefault(itemName);

            if (thisSC.Capture != null) {  // stop the old capture
                thisSC.Capture.FrameCaptured -= OnFrameCapturedAsync;
                thisSC.Capture.StopCapture();
                thisSC.Capture.Dispose();
                thisSC.Capture = null;
            }

            // stop any old audio captures
            if (thisSC.AudioCapture != null)
            {
                thisSC.AudioCapture?.StopRecording();
                thisSC.AudioCapture?.Dispose();
                thisSC.AudioCapture = null;
            }

            thisSC.Capture = capture;

            // AUDIO - this is not working yet and may never work!!!
            // thisSC.AudioCapture = new WasapiLoopbackCapture();

            // thisSC.AudioCapture.DataAvailable += (s, e) =>
            // {
            //     float volume = CalculateVolume(e.Buffer, e.BytesRecorded);

            //     // get the scoutselector from the scoutselectorpanel on the UI Thread
            //     Dispatcher.Invoke(() =>
            //     {
            //         var selector = ScoutSelectorPanel.Children.OfType<ScoutSelector>().FirstOrDefault(x => x.ScoutName == itemName);
            //         if (selector != null)
            //         {
            //             selector.SetVolume(volume);
            //         }
            //     });

            //     if (volume > NOISE_THRESHOLD)
            //     {
            //         LoudNoiseDetected?.Invoke(item, wormhole);
            //         Debug.WriteLine($"Loud noise detected! Level: {volume:F2}");
            //     }
            // };
            // thisSC.AudioCapture.RecordingStopped += (s, e) =>
            // {
            //     thisSC.AudioCapture?.Dispose();
            //     thisSC.AudioCapture = null;
            // };
                
            // thisSC.AudioCapture.StartRecording();
            // AUDIO - this is not working yet

            _scoutInfo.Add(thisSC);

            var rect = thisSC.Margins;
            TopTextBox.Value = (int)rect.Top;
            LeftTextBox.Value = (int)rect.Left;
            RightTextBox.Value = (int)rect.Right;
            BottomTextBox.Value = (int)rect.Bottom;

            // try to send a keep alive 
            SendKeepAliveAsync(thisSC, wormhole).Start();

        }

        private void StopScout(string key)
        {
            if (_scoutInfo.ContainsKey(key))
            {
                var scout = _scoutInfo.Get(key);
                scout.StopCapture();
                _scoutInfo.Remove(key);
            }
        }

        private async void OnFrameCapturedAsync(object sender, Bitmap bitmap)
        {

            if (_tesseract == null) return;

            // Check if we're already processing
            if (_isCapturingImage || _isDragging)
            {
                bitmap.Dispose();
                return;
            }

            BasicCapture src = (BasicCapture)sender;
            var thisItemName = src.GetItem().DisplayName;
            var thisScout = _scoutInfo.Get(thisItemName);

            if (thisScout == null)
            {
                bitmap.Dispose();
                return;
            }

            try
            {
                _isCapturingImage = true;
                src.PauseCapture();

                float scale = float.Parse(ValueOne.Text);
                int whSize = int.Parse(ValueThree.Text);
                float factor = float.Parse(ValueFour.Text);

                using (bitmap)
                {

                    var margins = thisScout.Margins;

                    var width = bitmap.Width - (int)(margins.Right + margins.Left) + whSize;
                    width = (int)Math.Floor(width / (double)whSize) * whSize;

                    var height = bitmap.Height - (int)(margins.Bottom + margins.Top) + whSize;
                    height = (int)Math.Floor(height / (double)whSize) * whSize;

                    var cropRect = new Rectangle(
                        (int)margins.Left - whSize / 2,
                        (int)margins.Top - whSize / 2,
                        width,
                        height
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
                            using (Bitmap croppedBitmap = new Bitmap(cropRect.Width, cropRect.Height))
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
                            tempPix = tempPix.Scale(scale, scale);  // 96 dpi * 4f = 384dpi  (which we set in the config)
                            tempPix = tempPix.BinarizeSauvola(whSize, factor, false);  //14, 0.2, false works with scale = 4f
                        }

                        return (tempBitmap, tempPix);
                    });

                    thisScout.LastImage = bitmapImage;

                    if (thisItemName == itemName)
                    {
                        CapturedImage.Source = bitmapImage;
                    }

                    // Check if the bitmap has changed
                    if (!pix.Equals(thisScout.LastCapture))
                    {
                        // Run OCR processing on a background thread and don't wait for it to finish
                        var task = Task.Run(async () =>
                        {
                            var text = "";
                            //var minx = 0;
                            //var miny = 0;
                            //var maxx = 0;
                            //var maxy = 0;
                            //var textFound = false;
                            //// OCR it once
                            //using (var page = _tesseract.Process(
                            //    pix,
                            //    PageSegMode.SingleBlock
                            //))
                            //{
                            //    Console.Write(page.GetTsvText(1));
                            //    minx = page.RegionOfInterest.Width;
                            //    miny = page.RegionOfInterest.Height;
                            //    maxx = 0;
                            //    maxy = 0;
                            //    foreach (var region in page.GetSegmentedRegions(PageIteratorLevel.Word))
                            //    {
                            //        Console.WriteLine(region.ToString()); 
                            //        textFound = true;
                            //        if (region.X < minx) minx = region.X;
                            //        if (region.Y < miny) miny = region.Y;
                            //        if (region.X + region.Width > maxx) maxx = region.X + region.Width;
                            //        if (region.Y + region.Height > maxy) maxy = region.Y + region.Height;
                            //    }
                            //    minx -= whSize;
                            //    miny -= whSize;
                            //    maxx += whSize;
                            //    maxy += whSize;
                            //    Console.WriteLine("REGION OF INTEREST: " + minx + ", " + miny + ", " + maxx + ", " + maxy);
                            //}

                            // third time's a charm
                            using (var page = _tesseract.Process(
                                pix,
                                //Tesseract.Rect.FromCoords(minx, miny, maxx, maxy),
                                PageSegMode.SingleBlock
                            ))
                            {
                                Console.Write(page.GetTsvText(1));

                                text = page.GetText();

                                await Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    foreach(ScoutSelector scout in ScoutSelectorPanel.Children)
                                    {
                                        if (scout.SelectedProcess?.MainWindowTitle == thisScout.Key)
                                        {
                                            scout.HasWormhole(text.Contains("Wormhole "));
                                        }
                                    }
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

                                thisScout.LastReportTime = DateTime.Now.Ticks;
                                thisScout.LastCapture = pix.Clone();
                            }
                        });


                    } else if (DateTime.Now.Ticks - thisScout.LastReportTime > KEEP_ALIVE_INTERVAL)
                    {
                        // send a keep-alive every 5 mins
                        await SendKeepAliveAsync(thisScout, src.Wormhole);
                    }
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
                _isCapturingImage = false;
            }
        }

        private async Task SendKeepAliveAsync(ScoutCapture thisScout, string wormhole)
        {
            var message = new ScoutMessage
            {
                Message = $@"{{ ""KEEPALIVE"": ""true"", ""Version"": ""{GetVersion()}"" }}",
                Scout = thisScout.Key.Substring(6),
                Wormhole = wormhole
            };

            var json = JsonConvert.SerializeObject(message);

            using (var client = new HttpClient())
            {
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(SERVER_URL, content);
                var body = await response.Content.ReadAsStringAsync();
                Console.WriteLine(body);
            }

            thisScout.LastReportTime = DateTime.Now.Ticks;
        }

        private float CalculateVolume(byte[] buffer, int bytesRecorded)
        {
            float sum = 0;
            int samplesCount = bytesRecorded / 4; // 32-bit samples
            
            for (int i = 0; i < bytesRecorded; i += 4)
            {
                float sample = BitConverter.ToSingle(buffer, i);
                sum += sample * sample;
            }
            
            return (float)Math.Sqrt(sum / samplesCount);
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
                    item.SetSelected(false);
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

        private void myCanvas_LayoutUpdated(object sender, EventArgs e)
        {
            ReDrawCaptureRectangle();
        }
    }
}
