using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using TwitchBetBot.Models;
namespace TwitchBetBot.Services
{
    /// <summary>
    /// OAuth 2.0 (Authorization Code) для DonationAlerts.
    /// Токены сохраняются в config.json в зашифрованном виде (Windows DPAPI).
    /// </summary>
    public class DonationAlertsAuthService
    {
        private const string AuthorizeUrl = "https://www.donationalerts.com/oauth/authorize";
        private const string TokenUrl = "https://www.donationalerts.com/oauth/token";
        private const string UserUrl = "https://www.donationalerts.com/api/v1/user/oauth";
        private const string Scopes = "oauth-user-show oauth-donation-subscribe oauth-donation-index";
        private readonly AppConfig _config;
        private readonly HttpClient _http = new HttpClient();
        public event Action<string> OnLogMessage;
        public DonationAlertsAuthService(AppConfig config)
        {
            _config = config;
        }
        public bool HasCredentials =>
            !string.IsNullOrWhiteSpace(_config.DonationAlertsClientId) &&
            !string.IsNullOrWhiteSpace(_config.DonationAlertsClientSecret);
        public bool HasTokens => !string.IsNullOrWhiteSpace(_config.DonationAlertsAccessToken);
        /// <summary>Открывает окно авторизации DA и меняет code на токены. true — успех.</summary>
        public async Task<bool> AuthorizeAsync()
        {
            if (!HasCredentials)
            {
                Log("Не заданы Client ID / Client Secret приложения DonationAlerts");
                return false;
            }
            var tcs = new TaskCompletionSource<string>();
            // DA сверяет redirect_uri посимвольно; в приложении он регистрируется с завершающим слэшем
            string redirectUri = string.IsNullOrWhiteSpace(_config.DonationAlertsRedirectUri)
                ? "http://localhost:3000/"
                : _config.DonationAlertsRedirectUri.Trim();
            if (!redirectUri.EndsWith("/"))
                redirectUri += "/";
            var authWindow = new Window
            {
                Title = "Авторизация DonationAlerts",
                Width = 520,
                Height = 720,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            var webView = new WebView2();
            void Navigating(object sender, CoreWebView2NavigationStartingEventArgs e)
            {
                if (!e.Uri.StartsWith(redirectUri, StringComparison.OrdinalIgnoreCase))
                    return;
                e.Cancel = true;
                string code = null;
                try
                {
                    var query = new Uri(e.Uri).Query.TrimStart('?');
                    foreach (var part in query.Split('&'))
                    {
                        if (part.StartsWith("code=", StringComparison.OrdinalIgnoreCase))
                        {
                            code = Uri.UnescapeDataString(part.Substring("code=".Length));
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Не удалось разобрать redirect: {ex.Message}");
                }
                authWindow.Close();
                tcs.TrySetResult(code);
            }
            webView.NavigationStarting += Navigating;
            authWindow.Content = webView;
            authWindow.Closed += (s, e) => tcs.TrySetResult(null);
            authWindow.Show();
            try
            {
                await webView.EnsureCoreWebView2Async();
            }
            catch (Exception ex)
            {
                Log($"WebView2 недоступен: {ex.Message}");
                authWindow.Close();
                return false;
            }
            string url = $"{AuthorizeUrl}?client_id={Uri.EscapeDataString(_config.DonationAlertsClientId)}" +
                         $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                         $"&response_type=code" +
                         $"&scope={Uri.EscapeDataString(Scopes)}";
            webView.CoreWebView2.Navigate(url);
            string authCode = await tcs.Task;
            if (string.IsNullOrEmpty(authCode))
            {
                Log("Авторизация DonationAlerts отменена");
                return false;
            }
            var token = await RequestTokenAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = _config.DonationAlertsClientId,
                ["client_secret"] = _config.DonationAlertsClientSecret,
                ["redirect_uri"] = redirectUri,
                ["code"] = authCode
            });
            if (token == null)
                return false;
            StoreToken(token);
            Log("DonationAlerts авторизован, токены зашифрованы (DPAPI) и сохранены");
            var user = await GetUserAsync();
            if (user != null)
            {
                _config.DonationAlertsBroadcasterId = user.Id.ToString();
                _config.Save();
                Log($"DonationAlerts: {user.Name} (id {user.Id})");
            }
            return true;
        }
        /// <summary>Обновляет access-токен по refresh_token. true — токен валиден после вызова.</summary>
        public async Task<bool> EnsureValidTokenAsync(bool force = false)
        {
            if (!HasTokens)
                return false;
            bool expiringSoon = _config.DonationAlertsTokenExpiresAt != DateTime.MinValue &&
                                _config.DonationAlertsTokenExpiresAt <= DateTime.UtcNow.AddMinutes(5);
            if (!force && !expiringSoon)
                return true;
            if (string.IsNullOrWhiteSpace(_config.DonationAlertsRefreshToken))
            {
                Log("Нет refresh_token DonationAlerts — нужна повторная авторизация");
                return false;
            }
            Log("Обновление токена DonationAlerts...");
            var token = await RequestTokenAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _config.DonationAlertsRefreshToken,
                ["client_id"] = _config.DonationAlertsClientId,
                ["client_secret"] = _config.DonationAlertsClientSecret,
                ["scope"] = Scopes
            });
            if (token == null)
            {
                Log("Не удалось обновить токен DonationAlerts");
                return false;
            }
            StoreToken(token);
            Log("Токен DonationAlerts обновлён");
            return true;
        }
        /// <summary>Профиль пользователя + socket_connection_token для Centrifugo.</summary>
        public async Task<DaUser> GetUserAsync()
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, UserUrl))
                {
                    request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_config.DonationAlertsAccessToken}");
                    var response = await _http.SendAsync(request);
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        Log("DonationAlerts: 401, пробуем обновить токен");
                        if (await EnsureValidTokenAsync(force: true))
                            return await GetUserAsync();
                        return null;
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        Log($"DonationAlerts /user/oauth: {(int)response.StatusCode}");
                        return null;
                    }
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<DaUserResponse>(json)?.Data;
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка запроса профиля DonationAlerts: {ex.Message}");
                return null;
            }
        }
        /// <summary>Подписка на приватные каналы Centrifugo, возвращает токены каналов.</summary>
        public async Task<DaSubscribeChannel[]> SubscribeChannelsAsync(string clientId, params string[] channels)
        {
            try
            {
                var payload = JsonConvert.SerializeObject(new { channels, client = clientId });
                using (var request = new HttpRequestMessage(HttpMethod.Post, "https://www.donationalerts.com/api/v1/centrifuge/subscribe"))
                {
                    request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_config.DonationAlertsAccessToken}");
                    request.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                    var response = await _http.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                    {
                        Log($"DonationAlerts /centrifuge/subscribe: {(int)response.StatusCode}");
                        return null;
                    }
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<DaSubscribeResponse>(json)?.Channels;
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка подписки Centrifugo: {ex.Message}");
                return null;
            }
        }
        public void ClearTokens()
        {
            _config.DonationAlertsAccessToken = "";
            _config.DonationAlertsRefreshToken = "";
            _config.DonationAlertsTokenExpiresAt = DateTime.MinValue;
            _config.Save();
            Log("Токены DonationAlerts удалены");
        }
        private async Task<DaTokenResponse> RequestTokenAsync(Dictionary<string, string> form)
        {
            try
            {
                using (var content = new FormUrlEncodedContent(form))
                {
                    var response = await _http.PostAsync(TokenUrl, content);
                    var json = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        Log($"DonationAlerts /oauth/token: {(int)response.StatusCode} {json}");
                        return null;
                    }
                    return JsonConvert.DeserializeObject<DaTokenResponse>(json);
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка получения токена DonationAlerts: {ex.Message}");
                return null;
            }
        }
        private void StoreToken(DaTokenResponse token)
        {
            _config.DonationAlertsAccessToken = token.AccessToken ?? "";
            if (!string.IsNullOrEmpty(token.RefreshToken))
                _config.DonationAlertsRefreshToken = token.RefreshToken;
            long seconds = token.ExpiresIn > 0 ? token.ExpiresIn : 3600;
            // Ограничиваем абсурдно большие значения, чтобы не переполнить DateTime
            if (seconds > 60L * 60 * 24 * 3650) seconds = 60L * 60 * 24 * 3650;
            _config.DonationAlertsTokenExpiresAt = DateTime.UtcNow.AddSeconds(seconds);
            _config.Save();
        }
        private void Log(string message) => OnLogMessage?.Invoke($"[DA] {message}");
    }
}
