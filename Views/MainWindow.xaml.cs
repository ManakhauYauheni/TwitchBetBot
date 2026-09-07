using System;
using System.Windows;
using System.Windows.Controls;
using TwitchBetBot.ViewModels;
using TwitchBetBot.Models;
namespace TwitchBetBot.Views
{
    public partial class MainWindow : Window
    {
        public static MainWindow Instance;
        public MainWindow()
        {
            InitializeComponent();
            Instance = this;
            // Устанавливаем DataContext (MainViewModel сам создаётся)
            var vm = new MainViewModel();
            DataContext = vm;
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
            ToggleButtonText.Text = "Скрыть";
        }
        private void ShowTokenToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            AccessTokenPasswordBox.Password = AccessTokenTextBox.Text;
            AccessTokenTextBox.Visibility = Visibility.Collapsed;
            AccessTokenPasswordBox.Visibility = Visibility.Visible;
            ToggleButtonText.Text = "Показать";
        }

        private void ShowSecretToggle_Checked(object sender, RoutedEventArgs e)
        {
            SecretTextBox.Text = SecretPasswordBox.Password;
            SecretTextBox.Visibility = Visibility.Visible;
            SecretPasswordBox.Visibility = Visibility.Collapsed;
            SecretToggleButtonText.Text = "Скрыть";
        }

        private void ShowSecretToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            SecretPasswordBox.Password = SecretTextBox.Text;
            SecretTextBox.Visibility = Visibility.Collapsed;
            SecretPasswordBox.Visibility = Visibility.Visible;
            SecretToggleButtonText.Text = "Показать";
        }

        private void ShowDaAccessToggle_Checked(object sender, RoutedEventArgs e)
        {
            DaAccessTextBox.Text = DaAccessPasswordBox.Password;
            DaAccessTextBox.Visibility = Visibility.Visible;
            DaAccessPasswordBox.Visibility = Visibility.Collapsed;
            DaAccessToggleButtonText.Text = "Скрыть";
        }

        private void ShowDaAccessToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            DaAccessPasswordBox.Password = DaAccessTextBox.Text;
            DaAccessTextBox.Visibility = Visibility.Collapsed;
            DaAccessPasswordBox.Visibility = Visibility.Visible;
            DaAccessToggleButtonText.Text = "Показать";
        }

        private void ShowDaRefreshToggle_Checked(object sender, RoutedEventArgs e)
        {
            DaRefreshTextBox.Text = DaRefreshPasswordBox.Password;
            DaRefreshTextBox.Visibility = Visibility.Visible;
            DaRefreshPasswordBox.Visibility = Visibility.Collapsed;
            DaRefreshToggleButtonText.Text = "Скрыть";
        }

        private void ShowDaRefreshToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            DaRefreshPasswordBox.Password = DaRefreshTextBox.Text;
            DaRefreshTextBox.Visibility = Visibility.Collapsed;
            DaRefreshPasswordBox.Visibility = Visibility.Visible;
            DaRefreshToggleButtonText.Text = "Показать";
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
            ModeToggleText.Text = "Полный";
            if (DataContext is MainViewModel vm)
            {
                vm.SwitchToFullMode();
            }
            ShowTwitchSections(true);
        }
        private void ModeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            ModeToggleText.Text = "Трекер";
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
        private void PredictionType_Checked(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;
            if (checkBox == null) return;
            string tag = checkBox.Tag.ToString();
            PredictionType selectedType;
            switch (tag)
            {
                case "WinLose":
                    selectedType = PredictionType.WinLose;
                    break;
                case "FirstBlood":
                    selectedType = PredictionType.FirstBlood;
                    break;
                case "RoshanKill":
                    selectedType = PredictionType.RoshanKill;
                    break;
                case "FirstBloodThenWinLose":
                    selectedType = PredictionType.FirstBloodThenWinLose;
                    break;
                case "FirstBloodThenRoshanKill":
                    selectedType = PredictionType.FirstBloodThenRoshanKill;
                    break;
                default:
                    selectedType = PredictionType.WinLose;
                    break;
            }
            // Снимаем выделение с других чекбоксов
            if (checkBox != chkWinLose) chkWinLose.IsChecked = false;
            if (checkBox != chkFirstBlood) chkFirstBlood.IsChecked = false;
            if (checkBox != chkRoshanKill) chkRoshanKill.IsChecked = false;
            if (checkBox != chkFBThenWinLose) chkFBThenWinLose.IsChecked = false;
            if (checkBox != chkFBThenRoshanKill) chkFBThenRoshanKill.IsChecked = false;
            // Убеждаемся что текущий чекбокс отмечен
            checkBox.IsChecked = true;
            // Обновляем ViewModel
            if (DataContext is MainViewModel vm)
            {
                vm.SelectedPredictionType = selectedType;
            }
        }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                (DataContext as MainViewModel)?.ShutdownServices();
            }
            catch { }
            base.OnClosing(e);
        }
        public void SetPredictionTypeFromViewModel(PredictionType type)
        {
            chkWinLose.IsChecked = false;
            chkFirstBlood.IsChecked = false;
            chkRoshanKill.IsChecked = false;
            chkFBThenWinLose.IsChecked = false;
            chkFBThenRoshanKill.IsChecked = false;
            switch (type)
            {
                case PredictionType.WinLose:
                    chkWinLose.IsChecked = true;
                    break;
                case PredictionType.FirstBlood:
                    chkFirstBlood.IsChecked = true;
                    break;
                case PredictionType.RoshanKill:
                    chkRoshanKill.IsChecked = true;
                    break;
                case PredictionType.FirstBloodThenWinLose:
                    chkFBThenWinLose.IsChecked = true;
                    break;
                case PredictionType.FirstBloodThenRoshanKill:
                    chkFBThenRoshanKill.IsChecked = true;
                    break;
            }
        }
    }
}