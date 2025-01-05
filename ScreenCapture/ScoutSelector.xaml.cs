using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GridScout
{
    /// <summary>
    /// Interaction logic for ScoutSelector.xaml
    /// </summary>
    public partial class ScoutSelector : UserControl
    {

        // ScoutSelected event
        public event EventHandler ScoutSelected;
        public event EventHandler ShowScout;
        public event EventHandler StopScout;
        public event EventHandler DeleteScout;
        private Process _process;
        private bool _awaitingFrame;
        private bool _minimised;
        private bool _selected;
        private bool _hasWormhole;

        public ScoutSelector()
        {
            InitializeComponent();
        }

        public string ScoutName => _process?.MainWindowTitle;
        public Process SelectedProcess => _process;

        public string ScoutLabelContent
        {
            get { return (string)ScoutLabel.Content; }
            set { ScoutLabel.Content = value; }
        }

        private void UpdateStatus()
        {
            if (_process != null)
            {
                if (_minimised)
                {
                    this.Background = new SolidColorBrush(Colors.Red);
                    return;
                }
                //if (_awaitingFrame)
                //{
                //    this.Background = new SolidColorBrush(Colors.OrangeRed);
                //    return;
                //}
                if (!_hasWormhole)
                {
                    this.Background = new SolidColorBrush(Colors.Orange);
                    return;
                }
                if (_selected)
                {
                    this.Background = new SolidColorBrush(Colors.LightGray);
                    return;
                }
            }
            this.Background = new SolidColorBrush(Colors.Transparent);

        }

        public void SetSelected(bool selected)
        {
            _selected = selected;
            UpdateStatus();
        }

        public void SetAwaitingFrame(bool awaitingFrame)
        {
            _awaitingFrame = awaitingFrame;
            UpdateStatus();
        }

        public void SetMinimised(bool minimised)
        {
            _minimised = minimised;
            UpdateStatus();
        }

        public void HasWormhole(bool hasWormhole)
        {
            _hasWormhole = hasWormhole;
            UpdateStatus();
        }

        public void SetProcesses(ObservableCollection<Process> processes)
        {
            ClientSelector.ItemsSource = processes;
        }

        public bool TryGetNewVersionOfProcess()
        {
            // get the first process with a matching title but different ID
            var newProcess = (ClientSelector.ItemsSource as ObservableCollection<Process>).FirstOrDefault(
                x => x.MainWindowTitle == _process.MainWindowTitle && x.Id != _process.Id
            );
            if (newProcess != null)
            {
                _process = newProcess;
                return true;
            }
            return false;
        }

        public void ClearSelection()
        {
            ClientSelector.SelectedIndex = -1;  
            ClientSelector.Visibility = Visibility.Visible;
            ClientLabel.Visibility = Visibility.Hidden;
            ColumnThree.Width = GridLength.Auto;
        }

        private void ShowButton_Click(object sender, RoutedEventArgs e)
        {
            this.SetSelected(true);
            ShowScout?.Invoke(this, EventArgs.Empty);
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopScout?.Invoke(this, EventArgs.Empty);
            _process = null;
            ClientSelector.Visibility = Visibility.Visible;
            ClientSelector.SelectedIndex = -1;
            ClientLabel.Visibility = Visibility.Hidden;
            ColumnThree.Width = new GridLength(0);
            DeleteButton.Visibility = Visibility.Visible;
            StopButton.Visibility = Visibility.Hidden;
            ShowButton.Visibility = Visibility.Hidden;
            UpdateStatus();
        }

        private void ClientSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ClientSelector.SelectedIndex > -1)
            {
                _process = ClientSelector.SelectedItem as Process;
                ClientSelector.Visibility = Visibility.Hidden;
                ClientLabel.Visibility = Visibility.Visible;
                // remove "Eve - " from the start of the scout name
                ClientLabel.Content = _process.MainWindowTitle.Substring(6);
                ColumnThree.Width = GridLength.Auto;
                DeleteButton.Visibility = Visibility.Hidden;
                StopButton.Visibility = Visibility.Visible;
                ShowButton.Visibility = Visibility.Visible;
                _selected = true;
                UpdateStatus();
                ScoutSelected?.Invoke(this, EventArgs.Empty);
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteScout?.Invoke(this, EventArgs.Empty);
        }
    }
}
