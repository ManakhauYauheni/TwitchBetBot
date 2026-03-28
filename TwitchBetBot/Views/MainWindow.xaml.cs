using System;
using System.Windows;
using System.Windows.Controls;
using TwitchBetBot.ViewModels;

namespace TwitchBetBot.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();

            // По умолчанию режим трекера — скрываем Twitch-секции
            ShowTwitchSections(false);
        }

        private void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.LogText = "";
            }
        }

        private void CopyLogs_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                Clipboard.SetText(vm.LogText);
                MessageBox.Show("Логи скопированы в буфер обмена", "Копирование",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox != null)
            {
                textBox.ScrollToEnd();
            }
        }

        private void ShowTokenToggle_Checked(object sender, RoutedEventArgs e)
        {
            AccessTokenTextBox.Text = AccessTokenPasswordBox.Password;
            AccessTokenTextBox.Visibility = Visibility.Visible;
            AccessTokenPasswordBox.Visibility = Visibility.Collapsed;
            ToggleButtonText.Text = "🔒";
        }

        private void ShowTokenToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            AccessTokenPasswordBox.Password = AccessTokenTextBox.Text;
            AccessTokenTextBox.Visibility = Visibility.Collapsed;
            AccessTokenPasswordBox.Visibility = Visibility.Visible;
            ToggleButtonText.Text = "👁️";
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            if (!string.IsNullOrEmpty(AccessTokenTextBox.Text))
            {
                AccessTokenPasswordBox.Password = AccessTokenTextBox.Text;
            }
        }

        private void ModeToggle_Checked(object sender, RoutedEventArgs e)
        {
            ModeToggleText.Text = "🌊 Полный";
            if (DataContext is MainViewModel vm)
            {
                vm.SwitchToFullMode();
            }
            ShowTwitchSections(true);
        }

        private void ModeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            ModeToggleText.Text = "🎯 Трекер";
            if (DataContext is MainViewModel vm)
            {
                vm.SwitchToTrackerMode();
            }
            ShowTwitchSections(false);
        }

        private void ShowTwitchSections(bool show)
        {
            var visibility = show ? Visibility.Visible : Visibility.Collapsed;

            AuthSection.Visibility = visibility;
            ChatBotSection.Visibility = visibility;
            PredictionSection.Visibility = visibility;
            ConnectionStatusPanel.Visibility = visibility;
        }
    }
}