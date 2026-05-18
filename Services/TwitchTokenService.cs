using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using TwitchBetBot.Models;

namespace TwitchBetBot.Services
{
    public class TwitchTokenService
    {
        private readonly AppConfig _config;
        private TaskCompletionSource<string> _tcs;
        private WebView2 _webView;
        private Window _authWindow;

        // Параметры авторизации
        private const string ClientId = "a633udyf447yrm4pckr51v65rnlqkj";
        private const string RedirectUri = "http://localhost:3000";
        private const string Scope = "channel:read:predictions+channel:manage:predictions";

        public TwitchTokenService(AppConfig config)
        {
            _config = config;
        }

        public async Task<string> GetAccessTokenAsync()
        {
            _tcs = new TaskCompletionSource<string>();

            // Создаём окно авторизации
            _authWindow = new Window
            {
                Title = "Авторизация Twitch",
                Width = 500,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            _webView = new WebView2();
            _webView.NavigationStarting += OnNavigationStarting;

            _authWindow.Content = _webView;
            _authWindow.Show();

            await _webView.EnsureCoreWebView2Async();

            // Формируем URL для авторизации
            string authUrl = $"https://id.twitch.tv/oauth2/authorize?client_id={ClientId}&redirect_uri={RedirectUri}&response_type=token&scope={Scope}";
            _webView.CoreWebView2.Navigate(authUrl);

            return await _tcs.Task;
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            // Проверяем, не перенаправление ли это на наш redirect_uri
            if (e.Uri.StartsWith(RedirectUri))
            {
                e.Cancel = true; // Останавливаем навигацию

                // Парсим токен из URL
                // URL выглядит так: http://localhost:3000/#access_token=xxx&scope=yyy&token_type=bearer
                var uri = new Uri(e.Uri);
                string fragment = uri.Fragment.TrimStart('#');

                if (!string.IsNullOrEmpty(fragment))
                {
                    var parameters = fragment.Split('&');
                    foreach (var param in parameters)
                    {
                        if (param.StartsWith("access_token="))
                        {
                            string token = param.Substring("access_token=".Length);

                            // Сохраняем токен в конфиг
                            _config.AccessToken = token;
                            _config.Save();

                            // Закрываем окно
                            _authWindow.Close();

                            // Возвращаем результат
                            _tcs.TrySetResult(token);
                            return;
                        }
                    }
                }

                _tcs.TrySetResult(null);
                _authWindow.Close();
            }
        }
    }
}