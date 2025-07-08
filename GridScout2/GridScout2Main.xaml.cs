using read_memory_64_bit;
using System.Diagnostics;
using System.Windows;
using static GridScout2.Eve;
using GameClient = read_memory_64_bit.GameClient;

namespace GridScout2
{

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // get the oneclick version
        private static readonly string _version = Environment.GetEnvironmentVariable("ClickOnce_CurrentVersion") ?? "2.0.0.DEV";

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;

            Title = $"GridScout v{MainWindow.Version}";
        }

        public static string Version => _version;

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await StartAsync();
        }

        private async Task StartAsync()
        {

            Debug.WriteLine("Starting...");

            GameClientCache.LoadCache(Properties.Settings.Default.uiRootAddressCache);

            await Task.WhenAll(ListGameClientProcesses()
                .Select(gc =>
                {
                    if (gc.mainWindowTitle.Length < 7)
                        return Task.CompletedTask;

                    // make a new Scout control for each process
                    Scout scout = new();

                    // add it to the UI
                    ScoutPanel.Children.Add(scout);

                    // fetch the cache incase we have a valid uiRootAddress
                    GameClient cachedGameClient = GameClientCache.GetGameClient(gc.processId, gc.mainWindowId);
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
                        GameClient cachedGameClient = GameClientCache.GetGameClient(gc.processId, gc.mainWindowId);
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