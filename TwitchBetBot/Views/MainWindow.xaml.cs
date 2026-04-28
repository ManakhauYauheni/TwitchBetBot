using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using TwitchBetBot.ViewModels;

namespace TwitchBetBot.Views
{
    public partial class MainWindow : Window
    {
        private System.Timers.Timer _cacheCleanupTimer;
        public static MainWindow Instance;
        private string _currentVideoTitle = "";
        private string _currentVideoUrl = "";
        private bool _isYouTubeInitialized = false;
        private bool _isYouTubeTabVisible = false;

        public MainWindow()
        {
            InitializeComponent();
            // Таймер для очистки кэша WebView2 каждые 3 минуты
_cacheCleanupTimer = new System.Timers.Timer(180000); // 180000 мс = 3 минуты
_cacheCleanupTimer.Elapsed += (s, e) =>
{
    Dispatcher.Invoke(() => CleanWebView2Cache());
};
_cacheCleanupTimer.AutoReset = true;
_cacheCleanupTimer.Start();
            CleanWebView2Cache();
            Instance = this;
            DataContext = new MainViewModel();

            ShowTwitchSections(false);

            // Инициализируем YouTube плеер после загрузки окна
            Loaded += async (s, e) => await InitializeYouTubePlayer();
        }

        private async Task InitializeYouTubePlayer()
        {
            try
            {
                string currentUrl = YouTubePlayer?.Source?.ToString() ?? "https://www.youtube.com";

                // Очищаем старый кэш
                CleanWebView2Cache();

                await YouTubePlayer.EnsureCoreWebView2Async(null);

                // Ждём полной загрузки страницы
                YouTubePlayer.CoreWebView2.DOMContentLoaded += async (s, e) =>
                {
                    string script = @"
                function getVideoInfo() {
                    const titleElement = document.querySelector('h1.ytd-video-primary-info-renderer yt-formatted-string');
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
                    if (info) {
                        window.chrome.webview.postMessage(JSON.stringify(info));
                    }
                }, 3000);
                
                // Отправляем текущее видео сразу
                const info = getVideoInfo();
                if (info) {
                    window.chrome.webview.postMessage(JSON.stringify(info));
                }
            ";

                    await YouTubePlayer.CoreWebView2.ExecuteScriptAsync(script);

                    if (DataContext is MainViewModel vm)
                    {
                        vm.Log("🎵 YouTube скрипт инициализирован");
                    }
                };

              
                YouTubePlayer.CoreWebView2.WebMessageReceived += OnWebMessage;

                // Устанавливаем начальный режим памяти
                if (YouTubeTab.IsSelected)
                {
                    YouTubePlayer.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                }
                else
                {
                    YouTubePlayer.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                }

                // Загружаем страницу
                YouTubePlayer.Source = new Uri(currentUrl);

                _isYouTubeInitialized = true;
              
                CurrentMusicStatus.Text = "🎵 YouTube плеер готов";

                if (DataContext is MainViewModel vm)
                {
                    vm.Log("🎵 YouTube плеер инициализирован");
                }
            }




            catch (Exception ex)
            {
                CurrentMusicStatus.Text = $"❌ Ошибка: {ex.Message}";
                if (DataContext is MainViewModel vm)
                {
                    vm.Log($"❌ Ошибка инициализации YouTube плеера: {ex.Message}");
                }
            }
        }


        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (YouTubePlayer?.CoreWebView2 == null) return;

            if (YouTubeTab.IsSelected)
            {
                // Вкладка YouTube выбрана
                YouTubePlayer.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                if (DataContext is MainViewModel vm)
                    vm.Log("🎵 YouTube плеер выбран, режим памяти Normal");
            }
            else
            {
                // Другая вкладка выбрана
                YouTubePlayer.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                if (DataContext is MainViewModel vm)
                    vm.Log("💤 YouTube плеер не выбран, режим памяти Low");
            }
        }
        private async Task CleanWebView2Cache()
        {
            if (YouTubePlayer?.CoreWebView2 != null)
            {
                try
                {
                    
                    var cacheKinds = CoreWebView2BrowsingDataKinds.CacheStorage;
                    await YouTubePlayer.CoreWebView2.Profile.ClearBrowsingDataAsync(cacheKinds);

                    
                    string script = @"
                if (window._musicInterval) clearInterval(window._musicInterval);
                
                function getVideoInfo() {
                    const titleElement = document.querySelector('h1.ytd-video-primary-info-renderer yt-formatted-string');
                    if (titleElement) {
                        return {
                            title: titleElement.textContent.trim(),
                            url: window.location.href
                        };
                    }
                    return null;
                }
                
                window._musicInterval = setInterval(() => {
                    const info = getVideoInfo();
                    if (info) {
                        window.chrome.webview.postMessage(JSON.stringify(info));
                    }
                }, 3000);
            ";
                    await YouTubePlayer.CoreWebView2.ExecuteScriptAsync(script);

                    CurrentMusicStatus.Text = "🎵 Кэш очищен";

                    if (DataContext is MainViewModel vm)
                    {
                        vm.Log("🧹 Кэш YouTube очищен (каждые 3 минуты)");
                    }
                }
                catch (Exception ex)
                {
                    if (DataContext is MainViewModel vm)
                    {
                        vm.Log($"⚠️ Ошибка очистки кэша: {ex.Message}");
                    }
                }
            }
        }


        private async void ClearCacheButton_Click(object sender, RoutedEventArgs e)
        {
            await CleanWebView2Cache();
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


        // Метод для доступа к текущему треку из MainViewModel
        public string GetCurrentMusicInfo()
        {
            if (string.IsNullOrEmpty(_currentVideoTitle))
                return "🎵 Сейчас ничего не играет";
            return $"🎵 {_currentVideoTitle} - {_currentVideoUrl}";
        }

        private async void RefreshMusicBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_isYouTubeInitialized)
            {
                await InitializeYouTubePlayer();
                return;
            }

            try
            {
                string script = @"
                    (() => {
                        const videoTitle = document.querySelector('.title.style-scope.ytd-video-primary-info-renderer');
                        if (videoTitle) {
                            return JSON.stringify({
                                title: videoTitle.textContent.trim(),
                                url: window.location.href
                            });
                        }
                        return null;
                    })();
                ";

                string result = await YouTubePlayer.CoreWebView2.ExecuteScriptAsync(script);
                if (!string.IsNullOrEmpty(result) && result != "null")
                {
                    string cleanResult = result.Trim('"');
                    var data = JObject.Parse(cleanResult);
                    string title = data["title"]?.ToString();
                    string url = data["url"]?.ToString();

                    if (!string.IsNullOrEmpty(title))
                    {
                        _currentVideoTitle = title;
                        _currentVideoUrl = url;
                        CurrentMusicStatus.Text = $"🎵 {_currentVideoTitle}";

                        if (DataContext is MainViewModel vm)
                        {
                            vm.Log($"🎵 Текущий трек: {_currentVideoTitle}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.Log($"❌ Ошибка обновления: {ex.Message}");
                }
            }
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


        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            _cacheCleanupTimer?.Stop();
            _cacheCleanupTimer?.Dispose();

        }


    }
}