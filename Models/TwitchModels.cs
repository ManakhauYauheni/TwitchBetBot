using System;
using Newtonsoft.Json;

namespace TwitchBetBot.Models
{
    // ========== Модели для работы с Twitch API ==========
    // Все эти классы нужны чтобы правильно разобрать JSON ответы от Twitch
    // JsonProperty указывает как называется поле в JSON ответе

    // Ответ от Twitch при проверке токена (https://id.twitch.tv/oauth2/validate)
    public class AuthValidation
    {
        [JsonProperty("client_id")]
        public string ClientId { get; set; } = "";  // ID приложения

        [JsonProperty("login")]
        public string Login { get; set; } = "";      // Логин пользователя

        [JsonProperty("user_id")]
        public string UserId { get; set; } = "";     // ID пользователя

        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }            // Сколько еще живет токен (секунд)
    }

    // Ответ от Twitch при запросе информации о пользователе
    public class UsersResponse
    {
        [JsonProperty("data")]
        public UserData[] Data { get; set; } = Array.Empty<UserData>(); // Массив пользователей
    }

    // Данные одного пользователя
    public class UserData
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";          // ID пользователя

        [JsonProperty("login")]
        public string Login { get; set; } = "";       // Логин (никнейм)

        [JsonProperty("display_name")]
        public string DisplayName { get; set; } = ""; // Имя для отображения
    }

    // Ответ от Twitch при запросе списка предсказаний
    public class PredictionResponse
    {
        [JsonProperty("data")]
        public TwitchPredictionData[] Data { get; set; } = Array.Empty<TwitchPredictionData>(); // Массив ставок
    }

    // Данные одного предсказания от Twitch API
    public class TwitchPredictionData
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";           // ID ставки

        [JsonProperty("broadcaster_id")]
        public string BroadcasterId { get; set; } = ""; // ID канала

        [JsonProperty("title")]
        public string Title { get; set; } = "";        // Вопрос ставки

        [JsonProperty("outcomes")]
        public TwitchOutcome[] Outcomes { get; set; } = Array.Empty<TwitchOutcome>(); // Варианты ответов

        [JsonProperty("status")]
        public string Status { get; set; } = "";       // Статус (ACTIVE, LOCKED и т.д.)

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }        // Когда создали

        [JsonProperty("ended_at")]
        public DateTime? EndedAt { get; set; }         // Когда завершили

        [JsonProperty("locked_at")]
        public DateTime? LockedAt { get; set; }        // Когда закрыли прием

        [JsonProperty("prediction_window")]
        public int PredictionWindowSeconds { get; set; } // Сколько времени принимали
    }

    // Данные одного варианта ответа
    public class TwitchOutcome
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";           // ID варианта

        [JsonProperty("title")]
        public string Title { get; set; } = "";        // Название (Radiant/Dire)

        [JsonProperty("color")]
        public string Color { get; set; } = "BLUE";    // Цвет в интерфейсе

        [JsonProperty("users")]
        public int Users { get; set; }                 // Сколько человек выбрали

        [JsonProperty("channel_points")]
        public int ChannelPoints { get; set; }          // Сколько баллов поставили
    }
}