using eve_parse_ui;
using read_memory_64_bit;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static GridScout2.Eve;

namespace GridScout2
{
    /// <summary>
    /// Interaction logic for Scout.xaml
    /// </summary>
    public partial class Scout : UserControl
    {
        private const long KEEP_ALIVE_INTERVAL = 5 * TimeSpan.TicksPerMinute; // 5 minutes in ticks
        private readonly string[] ALIVE_SPINNER = ["-", "\\", "|", "/"];
        private readonly string DISCONNECT_STRING = "Connection lost";
        private int aliveSpinnerIndex = 0;
        private GameClient? _gameClient;
        private long lastReportTime;
        private ScoutMessage? lastReportMessage;
        private Point? _mouseDownPoint;

        private const long GRID_CHANGE_NOTIFICATION_DURATION = 1 * TimeSpan.TicksPerMinute; // 1 minutes in ticks
        private long lastPilotCountChangeTime;
        private int lastPilotCount = 0;

        private string? _sigListSystemName;
        private readonly List<string> _sigCodes = [];
        private ParsedUserInterface? _uiRoot;

        // TODO: get this from the SDE
        private readonly List<int> cloakIDs = [11370, 11577, 11578, 14234, 14776,
            14778, 14780, 14782, 15790, 16126, 20561, 20563, 20565, 32260];

        public Scout()
        {
            InitializeComponent();
            Loaded += Scout_Loaded;
        }

        private async void Scout_Loaded(object sender, RoutedEventArgs e)
        {
            while (true)
            {
                try
                {
                    Background = new SolidColorBrush(Colors.Transparent);
                    Error.Content = "";
                    Error.Width = 0;
                    Error.Foreground = new SolidColorBrush(Colors.Red);
                    await WatchScout();
                }
                catch (Exception ex)
                {
                    Background = new SolidColorBrush(Colors.Red);
                    Error.Content = ex.Message;
                    Error.Width = Double.NaN;
                    Error.Foreground = new SolidColorBrush(Colors.White);
                    Debug.WriteLine(ex);

                    try
                    {
                        await Server.SendError(ex);
                    }
                    catch (Exception ex2)
                    {
                        Debug.WriteLine(ex2);
                    }
                }

                // wait 5 seconds
                await Task.Delay(5000);
            }
        }

        private async Task WatchScout()
        {
            while (true)
            {
                if (GameClient?.uiRootAddress != null)
                {
                    _uiRoot = await Task.Run(() =>
                    {
                        UITreeNode rootNode = MemoryReader.ReadMemory(GameClient.processId, GameClient.uiRootAddress)!;
                        if (rootNode == null) return null;
                        return UIParser.ParseUserInterface(rootNode);
                    });

                    if (_uiRoot == null)
                        break;
                    
                    // reset things
                    Error.Width = 0;
                    Wormhole.Width = 0;
                    Grid.Width = 0;
                    ShipStatus.Width = 0;

                    // Where are we?
                    var infoLocation = _uiRoot.InfoPanelContainer?.InfoPanelLocationInfo;
                    var probeScanner = _uiRoot.ProbeScanner;

                    string? currentSystemName = null;

                    if (infoLocation != null && infoLocation.CurrentSolarSystemName != null)
                    {
                        currentSystemName = infoLocation.CurrentSolarSystemName;
                        SolarSystem.Content =
                            $"{currentSystemName} ({string.Format("{0:0.0}", (double)(infoLocation.SecurityStatusPercent ?? 0) / 100)})";
                        SolarSystem.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(infoLocation.SecurityStatusColor ?? "#FF000000"));
                    }

                    // Are we docked?
                    var isDocked = _uiRoot.StationWindow != null;

                    if (isDocked)
                    {
                        Grid.Content = "Docked";
                        Grid.Width = Double.NaN;
                    }

                    var overviews = _uiRoot.OverviewWindows;

                    var gridscoutOverview = overviews
                        .Where(ow => ow.OverviewTabName.Equals("gridscout", StringComparison.CurrentCultureIgnoreCase))
                        .FirstOrDefault();

                    var isDisconnected = _uiRoot.MessageBoxes.Select(m => m.TextHeadline).Any(m => m?.Equals(DISCONNECT_STRING, StringComparison.CurrentCultureIgnoreCase) == true);

                    if (isDisconnected)
                    {
                        Background = new SolidColorBrush(Colors.Red);
                        Error.Content = "Connection Lost";
                        Error.Width = Double.NaN;
                        Error.Foreground = new SolidColorBrush(Colors.White);
                    }

                    if (gridscoutOverview == null)
                    {
                        Error.Content = "No GridScout Overview Found";
                        Error.Width = Double.NaN;
                    }
                    else
                    {
                        if (isDisconnected)
                        {
                            await SendDisconnectedReport(currentSystemName ?? "Unknown System");
                        }
                        else
                        {
                            if (
                                probeScanner != null
                                && !string.IsNullOrEmpty(currentSystemName)
                            )
                            {
                                var currentSigCodes = probeScanner.ScanResults
                                    .Select(result => result.CellsTexts?.GetValueOrDefault("ID"))
                                    .Where(sig => sig != null)
                                    .ToList();

                                // Did we change solar system?
                                if (!currentSystemName.Equals(_sigListSystemName))
                                {
                                    // reset the sig list
                                    _sigCodes.Clear();
                                    currentSigCodes
                                        .ToList()
                                        .ForEach(sig => _sigCodes.Add(sig!));

                                    // note the system these sigs belong to
                                    _sigListSystemName = currentSystemName;
                                }
                                else
                                {
                                    // we didn't change solar system
                                    // so check whether the sig list changed

                                    // find the changes in sig list
                                    var added = currentSigCodes.Except(_sigCodes).ToList();
                                    var removed = _sigCodes.Except(currentSigCodes).ToList();

                                    // if the sig list has changed
                                    if (added.Count > 0 || removed.Count > 0)
                                    {
                                        // reset the sig list
                                        _sigCodes.Clear();
                                        _sigCodes.AddRange(currentSigCodes!);
                                    }

                                    if (added.Count > 0)
                                    {
                                        // New Sig!!!
                                        lastPilotCountChangeTime = DateTime.Now.Ticks;
                                        ScanChanges.Content += "New Sig: " + string.Join(", ", added);
                                        ScanChanges.Width = Double.NaN;
                                    }

                                    if (removed.Count > 0)
                                    {
                                        // Removed Sig!!!
                                        lastPilotCountChangeTime = DateTime.Now.Ticks;
                                        ScanChanges.Content += "Sig Removed: " + string.Join(", ", removed);
                                        ScanChanges.Width = Double.NaN;
                                    }
                                }
                            }

                            var shipUI = _uiRoot.ShipUI;

                            var wormholeCode = gridscoutOverview.Entries
                                .Where(e =>
                                    e.ObjectType?.StartsWith("Wormhole ", StringComparison.CurrentCultureIgnoreCase) == true
                                )
                                .Select(wormhole => wormhole?.ObjectName?.Substring(9))
                                .SingleOrDefault("")!;

                            if (string.IsNullOrEmpty(wormholeCode))
                            {
                                Wormhole.Content = "No Wormhole";
                                Wormhole.Width = Double.NaN;
                            }
                            else
                            {
                                Wormhole.Content = wormholeCode;
                                Wormhole.Width = Double.NaN;

                                var isCloaked = shipUI?.ModuleButtons
                                    .Where(button => cloakIDs.Contains(button.TypeID ?? 0))
                                    .FirstOrDefault()?.IsActive == true;

                                if (!isCloaked)
                                {
                                    ShipStatus.Content = "NOT Cloaked!";
                                    ShipStatus.Width = Double.NaN;
                                }

                                var pilotCount = gridscoutOverview.Entries
                                    .Where(e =>
                                        e.ObjectType?.StartsWith("Wormhole ", StringComparison.CurrentCultureIgnoreCase) != true
                                    )
                                    .Count();

                                if (pilotCount == 0)
                                {
                                    Grid.Content = "No pilots on grid";
                                    Grid.Width = Double.NaN;
                                }
                                else
                                {
                                    Grid.Content = $"{pilotCount} pilot{(pilotCount > 1 ? "s" : "")} on grid";
                                    Grid.Width = Double.NaN;
                                }

                                if (pilotCount != lastPilotCount)
                                {
                                    lastPilotCount = pilotCount;
                                    lastPilotCountChangeTime = DateTime.Now.Ticks;
                                }
                            }

                            // send the report
                            await SendReport(gridscoutOverview, wormholeCode);
                        }
                    }

                    long deltaTime = DateTime.Now.Ticks - lastPilotCountChangeTime;
                    if (deltaTime < GRID_CHANGE_NOTIFICATION_DURATION)
                    {
                        // Lerp the colour from orange to transparent over time
                        byte alpha = (byte)(255f - (255f * (double)deltaTime / GRID_CHANGE_NOTIFICATION_DURATION));
                        Background = new SolidColorBrush(Color.FromArgb(alpha, 255, 128, 0));
                    }
                    else
                    {
                        ScanChanges.Content = "";
                        ScanChanges.Width = 0;
                    }
                }

                // wait for a bit
                await Task.Delay(700 + Random.Shared.Next(100));

                // update alive indicator
                aliveSpinnerIndex = (aliveSpinnerIndex + 1) % ALIVE_SPINNER.Length;
                Alive.Content = ALIVE_SPINNER[aliveSpinnerIndex];
            }
        }

        internal GameClient? GameClient { 
            get
            {
                return _gameClient;
            }
        }

        internal async Task StartAsync(GameClientProcessSummaryStruct gc, GameClient cachedGameClient)
        {
            cachedGameClient.mainWindowTitle = gc.mainWindowTitle;
            Character.Content = gc.mainWindowTitle.Length > 6 ? gc.mainWindowTitle.Substring(6) : gc.mainWindowTitle;

            if (cachedGameClient.uiRootAddress == 0)
            { 
                await FindUIRootAddress(gc, cachedGameClient);
            }            

            _gameClient = cachedGameClient;
            MemoryScanPanel.Width = 0;
            DetailsPanel.Width = Double.NaN;
        }

        private async Task FindUIRootAddress(GameClientProcessSummaryStruct gc, GameClient cachedGameClient)
        {
            DetailsPanel.Width = 0;
            MemoryScanPanel.Width = Double.NaN;

            var address = await Task.Run(() => MemoryReader.FindUIRootAddressFromProcessId(gc.processId));
            if (address != null)
            {
                cachedGameClient.mainWindowTitle = gc.mainWindowTitle;
                cachedGameClient.uiRootAddress = (ulong)address;
                Debug.WriteLine("Got uiRoot = " + address);
                GameClientCache.SaveCache();
            }
        }

        private async Task SendDisconnectedReport(string systemName)
        {
            ScoutMessage message = new()
            {
                Message = "",
                Scout = Character.Content.ToString() ?? "No Name",
                System = _sigListSystemName ?? systemName,
                Wormhole = "Lost Connection",
                Entries = [],
                Disconnected = true,
                Version = MainWindow.Version
            };

            await SendIfChangedOrOld(message);
        }

        private async Task SendReport(OverviewWindow gridscoutOverview, string wormhole)
        {
            var text = gridscoutOverview.Entries
                //.Where(e => e.ObjectType?.StartsWith("Wormhole ", StringComparison.CurrentCultureIgnoreCase) != true)
                .Select(e => e.ObjectType + " " + e.ObjectCorporation + " " + e.ObjectAlliance + " " + e.ObjectName)
                .DefaultIfEmpty(string.Empty)
                .Aggregate((a, b) => a + "\n" + b);

            var entries = gridscoutOverview.Entries
                .Select(e => new ScoutEntry()
                {
                    Type = e.ObjectType,
                    Corporation = e.ObjectCorporation,
                    Alliance = e.ObjectAlliance,
                    Name = e.ObjectName,
                    Distance = (e.ObjectDistanceInMeters ?? 0).ToString(),
                    Velocity = (e.ObjectVelocity ?? 0).ToString()
                })
                .ToList();

            ScoutMessage message = new()
            {
                Message = text,
                Scout = Character.Content.ToString() ?? "No Name",
                System = _sigListSystemName ?? "Unknown System",
                Wormhole = wormhole,
                Entries = entries,
                Disconnected = false,
                Version = MainWindow.Version
            };

            await SendIfChangedOrOld(message);
        }

        private async Task SendIfChangedOrOld(ScoutMessage message)
        {
            // if the message has changed or it's been a while, send it
            if (
                !message.MyEquals(lastReportMessage)
                || lastReportTime < DateTime.Now.Ticks - KEEP_ALIVE_INTERVAL
            )
            {
                await Server.SendReport(message);

                lastReportMessage = message;
                lastReportTime = DateTime.Now.Ticks;
            }
        }

        private void UserControl_MouseEnter(object sender, MouseEventArgs e)
        {
            BaseGrid.Background = new SolidColorBrush(Colors.LightBlue);
        }

        private void UserControl_MouseLeave(object sender, MouseEventArgs e)
        {
            BaseGrid.Background = new SolidColorBrush(Colors.Transparent);
        }

        private void UserControl_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _mouseDownPoint = e.GetPosition(this);
        }

        private async void UserControl_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (
                _mouseDownPoint != null 
                && _mouseDownPoint.Equals(e.GetPosition(this))
                && _gameClient != null
            )
            {
                WinApi.ShowWindow((nint)_gameClient.mainWindowId);

                // open a new Visualise window
                if (_uiRoot != null && Debugger.IsAttached)
                {
                    var visualise = new VisualiseUI();
                    visualise.Show();
                    await visualise.VisualiseAsync(_uiRoot);
                }
            }
            _mouseDownPoint = null;
        }
    }
}
