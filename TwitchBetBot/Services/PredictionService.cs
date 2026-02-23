using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TwitchBetBot.Models;

namespace TwitchBetBot.Services
{
    // Сервис для работы со ставками (предсказаниями) через Twitch API
    // Умеет создавать, закрывать, завершать и отменять ставки
    public class PredictionService
    {
        private readonly HttpClient _httpClient;      // Клиент для HTTP запросов к Twitch
        private readonly AppConfig _config;           // Настройки (токен, ID канала)
        private Prediction _currentPrediction;        // Текущая активная ставка

        // Свойство для доступа к текущей ставке из других классов
        public Prediction CurrentPrediction => _currentPrediction;

        // События, на которые подписывается MainViewModel чтобы знать об изменениях
        public event EventHandler<Prediction> OnPredictionCreated;  // Ставка создана
        public event EventHandler<Prediction> OnPredictionUpdated;  // Ставка обновлена (закрыта)
        public event EventHandler<Prediction> OnPredictionEnded;    // Ставка завершена/отменена

        // Конструктор - вызывается при создании сервиса
        public PredictionService(AppConfig config)
        {
            _config = config;
            _httpClient = new HttpClient();
            // Добавляем заголовки, которые требуются Twitch API
            _httpClient.DefaultRequestHeaders.Add("Client-Id", config.ClientId);
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.AccessToken}");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        // Создание новой ставки
        public async Task<Prediction> CreatePredictionAsync(string title, string[] outcomes, int windowSeconds = 300)
        {
            try
            {
                // Формируем запрос как требует Twitch API
                var request = new
                {
                    broadcaster_id = _config.BroadcasterId,      // ID канала
                    title = title,                                // Вопрос ставки
                    outcomes = new[]                              // Варианты ответов
                    {
                        new { title = outcomes[0] },
                        new { title = outcomes[1] }
                    },
                    prediction_window = windowSeconds            // Сколько времени принимать
                };

                // Превращаем в JSON
                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Отправляем POST запрос в Twitch
                var response = await _httpClient.PostAsync(
                    "https://api.twitch.tv/helix/predictions", content);

                if (response.IsSuccessStatusCode)
                {
                    // Если успешно - парсим ответ
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<PredictionResponse>(responseJson);

                    if (result?.Data?.Length > 0)
                    {
                        // Превращаем ответ Twitch в нашу модель Prediction
                        _currentPrediction = MapToPrediction(result.Data[0]);
                        // Сообщаем всем, что ставка создана
                        OnPredictionCreated?.Invoke(this, _currentPrediction);
                        return _currentPrediction;
                    }
                }
            }
            catch (Exception)
            {
                
            }
            return null;
        }

        // Закрытие приема ставок (LOCKED)
        public async Task<bool> LockPredictionAsync()
        {
            if (_currentPrediction == null) return false;

            try
            {
                var request = new
                {
                    broadcaster_id = _config.BroadcasterId,
                    id = _currentPrediction.Id,
                    status = "LOCKED"      // Меняем статус на LOCKED
                };

                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Twitch API использует PATCH для обновления
                var response = await _httpClient.PatchAsync(
                    "https://api.twitch.tv/helix/predictions", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<PredictionResponse>(responseJson);

                    if (result?.Data?.Length > 0)
                    {
                        _currentPrediction = MapToPrediction(result.Data[0]);
                        OnPredictionUpdated?.Invoke(this, _currentPrediction);
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // Игнорируем ошибки
            }
            return false;
        }

        // Завершение ставки с победителем (RESOLVED)
        public async Task<bool> EndPredictionAsync(string winningOutcomeId)
        {
            if (_currentPrediction == null) return false;

            try
            {
                var request = new
                {
                    broadcaster_id = _config.BroadcasterId,
                    id = _currentPrediction.Id,
                    status = "RESOLVED",              // Статус "завершена"
                    winning_outcome_id = winningOutcomeId  // ID победившего варианта
                };

                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PatchAsync(
                    "https://api.twitch.tv/helix/predictions", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<PredictionResponse>(responseJson);

                    if (result?.Data?.Length > 0)
                    {
                        _currentPrediction = MapToPrediction(result.Data[0]);
                        OnPredictionEnded?.Invoke(this, _currentPrediction);
                        return true;
                    }
                }
            }
            catch (Exception)
            {
               
            }
            return false;
        }

        // Отмена ставки (возврат баллов)
        public async Task<bool> CancelPredictionAsync()
        {
            if (_currentPrediction == null) return false;

            try
            {
                var request = new
                {
                    broadcaster_id = _config.BroadcasterId,
                    id = _currentPrediction.Id,
                    status = "CANCELED"      // Статус "отменена"
                };

                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PatchAsync(
                    "https://api.twitch.tv/helix/predictions", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<PredictionResponse>(responseJson);

                    if (result?.Data?.Length > 0)
                    {
                        _currentPrediction = MapToPrediction(result.Data[0]);
                        OnPredictionEnded?.Invoke(this, _currentPrediction);
                        return true;
                    }
                }
            }
            catch (Exception)
            {
               
            }
            return false;
        }

        // Принудительное обновление данных (сброс кэша и запрос к API)
        public async Task ForceRefresh()
        {
            try
            {
                // Очищаем кэш
                _currentPrediction = null;

                // Запрашиваем свежие данные с Twitch
                var response = await _httpClient.GetAsync(
                    $"https://api.twitch.tv/helix/predictions?broadcaster_id={_config.BroadcasterId}");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<PredictionResponse>(json);

                    LogToConsole($"🔄 ForceRefresh: получено {result?.Data?.Length ?? 0} ставок");

                    if (result?.Data?.Length > 0)
                    {
                        foreach (var data in result.Data)
                        {
                            LogToConsole($"   - ID: {data.Id}, Статус: {data.Status}, Заголовок: {data.Title}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogToConsole($"❌ ForceRefresh ошибка: {ex.Message}");
            }
        }

        // Логирование в консоль
        private void LogToConsole(string message)
        {
            Console.WriteLine($"[PredictionService] {DateTime.Now:HH:mm:ss} {message}");
        }

        // Получение текущей активной ставки
        public async Task<Prediction> GetCurrentPredictionAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync(
                    $"https://api.twitch.tv/helix/predictions?broadcaster_id={_config.BroadcasterId}");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<PredictionResponse>(json);

                    if (result?.Data?.Length > 0)
                    {
                        var prediction = MapToPrediction(result.Data[0]);

                        // Только активные или заблокированные ставки
                        if (prediction.Status == PredictionStatus.ACTIVE ||
                            prediction.Status == PredictionStatus.LOCKED)
                        {
                            _currentPrediction = prediction;
                            return prediction;
                        }
                        else
                        {
                            // Завершенные/отмененные не считаем текущими
                            _currentPrediction = null;
                            return null;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Игнорируем ошибки
            }
            return null;
        }

        // Превращает данные из Twitch API в нашу внутреннюю модель Prediction
        private Prediction MapToPrediction(TwitchPredictionData apiData)
        {
            var prediction = new Prediction
            {
                Id = apiData.Id,
                BroadcasterId = apiData.BroadcasterId,
                Title = apiData.Title,
                Status = Enum.Parse<PredictionStatus>(apiData.Status.ToUpper()),
                CreatedAt = apiData.CreatedAt,
                PredictionWindowSeconds = apiData.PredictionWindowSeconds
            };

            foreach (var outcome in apiData.Outcomes)
            {
                prediction.Outcomes.Add(new PredictionOutcome
                {
                    Id = outcome.Id,
                    Title = outcome.Title,
                    Color = outcome.Color,
                    Users = outcome.Users,
                    ChannelPoints = outcome.ChannelPoints
                });
            }

            return prediction;
        }
    }
}