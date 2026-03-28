using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace TwitchBetBot.Services
{
    public class OpenDotaService
    {
        private readonly HttpClient _httpClient = new();
        private readonly Action<string> _log;

        public OpenDotaService(Action<string> log = null)
        {
            _log = log;
        }

        private void Log(string message)
        {
            _log?.Invoke($"[OpenDota] {message}");
        }

        public async Task<(bool isRanked, bool radiantWin)> GetMatchInfo(long matchId)
        {
            int maxAttempts = 3;
            int delaySeconds = 30;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    string url = $"https://api.opendota.com/api/matches/{matchId}";
                    var response = await _httpClient.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var data = JObject.Parse(json);

                        int lobbyType = data["lobby_type"]?.Value<int>() ?? -1;
                        bool radiantWin = data["radiant_win"]?.Value<bool>() ?? false;

                        // Если данные есть (lobby_type не -1), возвращаем результат
                        if (lobbyType != -1)
                        {
                            return (lobbyType == 7, radiantWin);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Ошибка запроса: {ex.Message}");
                }

                if (attempt < maxAttempts)
                {
                    Log($"Данные ещё не готовы, попытка {attempt + 1} через {delaySeconds} сек...");
                    await Task.Delay(delaySeconds * 1000);
                }
            }

            Log($"Не удалось получить данные после {maxAttempts} попыток");
            return (false, false);
        }
    }
}