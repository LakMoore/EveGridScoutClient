using eve_parse_ui;
using System.Diagnostics;
using System.IO;
using System.Text;
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


namespace GridScout2
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static string SERVER_URL =>
            // if we are running from the IDE   
            Debugger.IsAttached 
                //? "https://ffew.space/gridscout/"
                ? "http://localhost:3000/" 
                : "https://ffew.space/gridscout/";

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await StartAsync();
        }

        public static string ServerURL => SERVER_URL;

        private async Task StartAsync()
        {

            Debug.WriteLine("Starting...");

            GameClientCache.LoadCache();

            await Task.WhenAll(ListGameClientProcesses()
                .Select(gc =>
                {
                    // make a new Scout control for each process
                    Scout scout = new();

                    // add it to the UI
                    ScoutPanel.Children.Add(scout);

                    // fetch the cache incase we have a valid uiRootAddress
                    GameClient cachedGameClient = GameClientCache.GetGameClient(gc.processId);
                    return scout.StartAsync(gc, cachedGameClient);
                })
                .ToList());

            Debug.WriteLine("Loaded.");

            do
            {
                var processes = ListGameClientProcesses();
                await Task.WhenAll(processes
                .Select(gc =>
                {
                    if (gc.mainWindowTitle.Length < 7)
                        return Task.CompletedTask;

                    // Do we already have a Scout control for this process?
                    var existingScout = ScoutPanel.Children.Cast<Scout>()
                        .Where(sc => sc.GameClient?.processId == gc.processId)
                        .ToList();

                    if (existingScout.Count < 1)
                    {
                        // make a new Scout control for each process
                        Scout scout = new();

                        // add it to the UI
                        ScoutPanel.Children.Add(scout);

                        // fetch the cache incase we have a valid uiRootAddress
                        GameClient cachedGameClient = GameClientCache.GetGameClient(gc.processId);
                        return scout.StartAsync(gc, cachedGameClient);
                    }

                    return Task.CompletedTask;
                })
                .ToList());

                ScoutPanel.Children.Cast<Scout>()
                    .Where(sc => !processes.Any(gc => gc.mainWindowTitle == sc.GameClient?.mainWindowTitle))
                    .ToList()
                    .ForEach(sc => ScoutPanel.Children.Remove(sc));

                await Task.Delay(1500);
            }
            while (true);
        }
    }
}