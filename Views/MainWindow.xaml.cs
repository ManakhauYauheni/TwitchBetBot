using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TwitchBetBot.ViewModels;
using TwitchBetBot.Models;

namespace TwitchBetBot.Views
{
    public partial class MainWindow : Window
    {
      
        public static MainWindow Instance;
        private string _currentVideoTitle = "";
        private string _currentVideoUrl = "";
        private bool _isYouTubeInitialized = false;

        public MainWindow()
        {
            InitializeComponent();

          

          
            Instance = this;

            // Устанавливаем DataContext (MainViewModel сам создаётся)
            var vm = new MainViewModel();
            DataContext = vm;

            ShowTwitchSections(false);

            Loaded += async (s, e) => await InitializeYouTubePlayer();
        }

        private async Task InitializeYouTubePlayer()
        {
            try
            {
                var mainVm = DataContext as MainViewModel;

                // Инициализируем плеер напрямую из Windows (Evergreen Runtime)
                await YouTubePlayer.EnsureCoreWebView2Async();
                mainVm?.Log($"✅ WebView2 успешно инициализирован из системного Runtime");

                // Универсальный скрипт автопропуска рекламы
                string smartAdSkipper = @"
(function() {
    setInterval(() => {
        const moviePlayer = document.getElementById('movie_player');
        const video = document.querySelector('video');
        
        if (moviePlayer && video && (moviePlayer.classList.contains('ad-showing') || moviePlayer.classList.contains('ad-interrupting'))) {
            // Ускоряем и перематываем рекламу
            video.muted = true;
            video.playbackRate = 16.0;
            if (video.duration && isFinite(video.duration)) { 
                video.currentTime = video.duration - 0.01; 
            }
            
            // Пытаемся вызвать встроенный метод пропуска
            if (typeof moviePlayer.skipAd === 'function') { 
                moviePlayer.skipAd(); 
            }
            
            // Универсальный поиск кнопки пропуска (по разным селекторам)
            let skipBtn = document.querySelector('button[aria-label=""Пропустить""], button[aria-label*=""Skip"" i], .ytp-skip-ad-button, .ytp-ad-skip-button');
            if (!skipBtn) {
                skipBtn = document.querySelector('.ytSpecButtonShapeNextHost');
            }
            if (skipBtn) {
                skipBtn.click();
                skipBtn.dispatchEvent(new Event('click', { bubbles: true }));
            }
        }
        
        // Удаляем рекламные баннеры
        const ads = document.querySelectorAll('ytd-ad-slot-renderer, #masthead-ad, .ad-container');
        ads.forEach(el => el.remove());
    }, 100);
})();
";
                await YouTubePlayer.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(smartAdSkipper);
                mainVm?.Log("🎵 Скрипт пропуска рекламы внедрён");

                // Скрипт для получения информации о текущем треке
                YouTubePlayer.CoreWebView2.DOMContentLoaded += async (s, e) =>
                {
                    string script = @"
function getVideoInfo() {
    const titleElement = document.querySelector('h1.ytd-video-primary-info-renderer yt-formatted-string, h1.ytd-watch-metadata yt-formatted-string');
    if (titleElement && titleElement.textContent) {
        return {
            title: titleElement.textContent.trim(),
            url: window.location.href
        };
    }
    return null;
}
if (window._musicInterval) clearInterval(window._musicInterval);
window._musicInterval = setInterval(() => {
    const info = getVideoInfo();
    if (info) { window.chrome.webview.postMessage(JSON.stringify(info)); }
}, 3000);
const info = getVideoInfo();
if (info) { window.chrome.webview.postMessage(JSON.stringify(info)); }
";
                    await YouTubePlayer.CoreWebView2.ExecuteScriptAsync(script);
                    mainVm?.Log("🎵 YouTube скрипт инициализирован");
                };

                YouTubePlayer.CoreWebView2.WebMessageReceived += OnWebMessage;

                // Настройка режима памяти
                if (YouTubeTab.IsSelected)
                    YouTubePlayer.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                else
                    YouTubePlayer.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;

                // Загружаем YouTube
                if (string.IsNullOrEmpty(YouTubePlayer.Source?.ToString()) || YouTubePlayer.Source.ToString() == "about:blank")
                {
                    YouTubePlayer.Source = new Uri("https://youtube.com");
                }

                _isYouTubeInitialized = true;
                CurrentMusicStatus.Text = "🎵 YouTube плеер готов (Smart AdBlock)";
                mainVm?.Log("🎵 YouTube плеер инициализирован");
            }
            catch (Exception ex)
            {
                CurrentMusicStatus.Text = $"❌ Ошибка: {ex.Message}";
                (DataContext as MainViewModel)?.Log($"❌ Ошибка инициализации YouTube плеера: {ex.Message}");
            }
        }


        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (YouTubePlayer?.CoreWebView2 == null) return;

            if (YouTubeTab.IsSelected)
            {
                YouTubePlayer.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                if (DataContext is MainViewModel vm)
                    vm.Log("🎵 YouTube плеер выбран, режим памяти Normal");
            }
            else
            {
                YouTubePlayer.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                if (DataContext is MainViewModel vm)
                    vm.Log("💤 YouTube плеер не выбран, режим памяти Low");
            }
        }
       
        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string json = e.TryGetWebMessageAsString();
                if (string.IsNullOrEmpty(json)) return;

                var data = JObject.Parse(json);
                string title = data["title"]?.ToString();
                string url = data["url"]?.ToString();

                if (!string.IsNullOrEmpty(title) && title != _currentVideoTitle)
                {
                    _currentVideoTitle = title;
                    _currentVideoUrl = url;

                    Dispatcher.Invoke(() =>
                    {
                        CurrentMusicStatus.Text = $"🎵 {_currentVideoTitle}";
                    });

                    if (DataContext is MainViewModel vm)
                    {
                        vm.Log($"🎵 YouTube трек обновлён: {_currentVideoTitle}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.Log($"⚠️ Ошибка получения трека: {ex.Message}");
                }
            }
        }

        public string GetCurrentMusicInfo()
        {
            if (string.IsNullOrEmpty(_currentVideoTitle))
                return "🎵 Сейчас ничего не играет";
            return $"🎵 {_currentVideoTitle} - {_currentVideoUrl}";
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
            // Останавливаем YouTube плеер
            if (YouTubePlayer?.CoreWebView2 != null)
            {
                try
                {
                    // Останавливаем видео через JavaScript
                    string stopScript = @"
                const video = document.querySelector('video');
                if (video) {
                    video.pause();
                    video.src = '';
                    video.load();
                }
            ";
                    YouTubePlayer.CoreWebView2.ExecuteScriptAsync(stopScript);

                    // Останавливаем WebView2 (есть такой метод)
                    YouTubePlayer.CoreWebView2.Stop();
                }
                catch { }
            }

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            // Уничтожаем WebView2 контрол
            if (YouTubePlayer != null)
            {
                try
                {
                    // Удаляем WebView2 из визуального дерева
                    YouTubePlayer.CoreWebView2?.Stop();
                    YouTubePlayer.Dispose();
                }
                catch { }
            }

         
          

            base.OnClosed(e);
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