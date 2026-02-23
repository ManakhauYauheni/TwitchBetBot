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
                // Прокручиваем вниз при добавлении текста
                textBox.ScrollToEnd();
            }
        }
        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }

        private void ShowTokenToggle_Checked(object sender, RoutedEventArgs e)
        {
            // Показываем токен
            AccessTokenTextBox.Text = AccessTokenPasswordBox.Password;
            AccessTokenTextBox.Visibility = Visibility.Visible;
            AccessTokenPasswordBox.Visibility = Visibility.Collapsed;
            ToggleButtonText.Text = "🔒";
        }

        private void ShowTokenToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            // Скрываем токен
            AccessTokenPasswordBox.Password = AccessTokenTextBox.Text;
            AccessTokenTextBox.Visibility = Visibility.Collapsed;
            AccessTokenPasswordBox.Visibility = Visibility.Visible;
            ToggleButtonText.Text = "👁️";
        }

        // Добавим синхронизацию при загрузке окна
        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            // При загрузке синхронизируем пароль с токеном
            if (!string.IsNullOrEmpty(AccessTokenTextBox.Text))
            {
                AccessTokenPasswordBox.Password = AccessTokenTextBox.Text;
            }
        }



    }
}