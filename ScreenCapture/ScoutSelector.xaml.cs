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

        public void NotSelected()
        {
            this.Background = new SolidColorBrush(Colors.Transparent);
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
            this.Background = new SolidColorBrush(Colors.LightGray);
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
            this.Background = new SolidColorBrush(Colors.Transparent);
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
                this.Background = new SolidColorBrush(Colors.LightGray);
                ScoutSelected?.Invoke(this, EventArgs.Empty);
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteScout?.Invoke(this, EventArgs.Empty);
        }
    }
}
