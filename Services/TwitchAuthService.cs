using System;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TwitchBetBot.Models;

namespace TwitchBetBot.Services
{
    // Сервис для работы с авторизацией Twitch
    // Проверяет токены и получает информацию о пользователях
    public class TwitchAuthService
    {
        private readonly HttpClient _httpClient;

        public TwitchAuthService()
        {
            _httpClient = new HttpClient();
            // Все ответы от Twitch обычно в JSON
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        // Проверяет, валиден ли токен доступа
        // Возвращает информацию о токене или null если токен невалидный
        public async Task<AuthValidation> ValidateToken(string accessToken)
        {
            try
            {
                // Удаляем старый заголовок авторизации если был
                _httpClient.DefaultRequestHeaders.Remove("Authorization");
                // Добавляем новый с переданным токеном
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

                // Twitch API для проверки токена
                var response = await _httpClient.GetAsync("https://id.twitch.tv/oauth2/validate");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<AuthValidation>(json);
                }
            }
            catch (Exception)
            {
                // Ошибки игнорируем - вернем null
            }
            return null;
        }

        // Получает ID канала по его имени
        // Нужно для создания ставок (требуется broadcaster_id)
        public async Task<string> GetBroadcasterId(string accessToken, string clientId, string channelName)
        {
            try
            {
                // Обновляем заголовки для запроса
                _httpClient.DefaultRequestHeaders.Remove("Authorization");
                _httpClient.DefaultRequestHeaders.Remove("Client-Id");
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
                _httpClient.DefaultRequestHeaders.Add("Client-Id", clientId);

                // Запрашиваем информацию о пользователе по логину
                var response = await _httpClient.GetAsync(
                    $"https://api.twitch.tv/helix/users?login={channelName}");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<UsersResponse>(json);

                    // Возвращаем ID первого найденного пользователя
                    if (result?.Data?.Length > 0)
                    {
                        return result.Data[0].Id;
                    }
                }
            }
            catch (Exception)
            {
                
            }
            return null;
        }
    }
}