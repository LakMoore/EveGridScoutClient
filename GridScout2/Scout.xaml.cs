using eve_parse_ui;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using read_memory_64_bit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml.Serialization;
using static GridScout2.Eve;
using static System.Net.Mime.MediaTypeNames;

namespace GridScout2
{
    /// <summary>
    /// Interaction logic for Scout.xaml
    /// </summary>
    public partial class Scout : UserControl
    {
        private const long KEEP_ALIVE_INTERVAL = 5 * TimeSpan.TicksPerMinute; // 5 minutes in ticks
        private readonly string[] ALIVE_SPINNER = ["-", "\\", "|", "/"];
        private int aliveSpinnerIndex = 0;
        private GameClient? _gameClient;
        private long lastReportTime;
        private string? lastReportMessage;
        private Point? _mouseDownPoint;

        public Scout()
        {
            InitializeComponent();
            Loaded += Scout_Loaded;
        }

        private async void Scout_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await WatchScout();
            }
            catch (Exception ex)
            {
                Background = new SolidColorBrush(Colors.Red);
                Error.Content = ex.Message;
                Error.Width = Double.NaN;
                Error.Foreground = new SolidColorBrush(Colors.White);
                Debug.WriteLine(ex);
            }
        }

        private async Task WatchScout()
        {
            while (true)
            {
                if (GameClient?.uiRootAddress != null)
                {
                    var uiRoot = await Task.Run(() =>
                    {
                        UITreeNode rootNode = MemoryReader.ReadMemory(GameClient.processId, GameClient.uiRootAddress)!;
                        return UIParser.ParseUserInterface(rootNode);
                    });

                    // reset things
                    Error.Width = 0;
                    Wormhole.Width = 0;
                    Grid.Width = 0;

                    var overviews = uiRoot.OverviewWindows;

                    var gridscoutOverview = overviews
                        .Where(ow => ow.OverviewTabName.Equals("gridscout", StringComparison.CurrentCultureIgnoreCase))
                        .FirstOrDefault();

                    var infoLocation = uiRoot.InfoPanelContainer?.InfoPanelLocationInfo;
                    if (infoLocation != null)
                    {
                        SolarSystem.Content = 
                            $"{infoLocation.CurrentSolarSystemName} ({string.Format("{0:0.0}", (double)(infoLocation.SecurityStatusPercent ?? 0) / 100)})";
                        SolarSystem.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(infoLocation.SecurityStatusColor ?? "#FF000000"));
                    }

                    if (gridscoutOverview != null)
                    {
                        var wormholeCode = gridscoutOverview.Entries
                            .Where(e => 
                                e.ObjectType?.StartsWith("Wormhole ", StringComparison.CurrentCultureIgnoreCase) == true
                            )
                            .Select(wormhole => wormhole?.ObjectName?.Substring(9))
                            .SingleOrDefault("No Wormhole")!;

                        Wormhole.Content = wormholeCode;
                        Wormhole.Width = Double.NaN;

                        var pilotCount = gridscoutOverview.Entries
                            .Where(e =>
                                e.ObjectType?.StartsWith("Wormhole ", StringComparison.CurrentCultureIgnoreCase) != true
                            )
                            .Count();

                        if (pilotCount == 0)
                        {
                            Grid.Content = "No pilots on grid";
                            Grid.Width = Double.NaN;
                        } else
                        {
                            Grid.Content = $"{pilotCount} pilot{(pilotCount > 1 ? "s" : "")} on grid";
                            Grid.Width = Double.NaN;
                        }

                        await MakeAndSendReport(gridscoutOverview, wormholeCode);

                    } else
                    {
                        Error.Content = "No GridScout Overview Found";
                        Error.Width = Double.NaN;
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
            Character.Content = gc.mainWindowTitle.Substring(6);

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

        private async Task MakeAndSendReport(OverviewWindow gridscoutOverview, string wormhole)
        {
            var text = gridscoutOverview.Entries
                //.Where(e => e.ObjectType?.StartsWith("Wormhole ", StringComparison.CurrentCultureIgnoreCase) != true)
                .Select(e => e.ObjectType + " " + e.ObjectCorporation + " " + e.ObjectAlliance + " " + e.ObjectName)
                .DefaultIfEmpty(string.Empty)
                .Aggregate((a, b) => a + "\n" + b);
            
            var message = new ScoutMessage
            {
                Message = text,
                Scout = Character.Content.ToString() ?? "No Name",
                Wormhole = wormhole
            };

            if (
                lastReportMessage == message.Message 
                && lastReportTime > DateTime.Now.Ticks - KEEP_ALIVE_INTERVAL
            ) {
                return;
            }

            var json = JsonConvert.SerializeObject(message);

            using (var client = new HttpClient())
            {
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(MainWindow.ServerURL, content);
                var body = await response.Content.ReadAsStringAsync();
                Console.WriteLine(body);
            }

            lastReportMessage = message.Message;
            lastReportTime = DateTime.Now.Ticks;
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

        private void UserControl_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (
                _mouseDownPoint != null 
                && _mouseDownPoint.Equals(e.GetPosition(this))
                && _gameClient != null
            )
            {
                WinApi.ShowWindow((nint)_gameClient.mainWindowId);
            }
            _mouseDownPoint = null;
        }
    }
}
