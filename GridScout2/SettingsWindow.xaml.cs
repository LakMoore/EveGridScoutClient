using System.Windows;

namespace GridScout2
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();

            ServerAddressText.Text = Properties.Settings.Default.ServerAddress ?? string.Empty;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.ServerAddress = ServerAddressText.Text ?? string.Empty;
            Properties.Settings.Default.Save();
            this.DialogResult = true;
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
