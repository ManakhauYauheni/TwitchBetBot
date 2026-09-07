using System;
using System.IO;
using Newtonsoft.Json;
using TwitchBetBot.Utils;
namespace TwitchBetBot.Models
{
    // Типы ставок
    public enum PredictionType
    {
        WinLose,               // Кто победит в матче
        FirstBlood,            // Первая кровь
        RoshanKill,                 // Первое поднятие RoshanKill
        FirstBloodThenWinLose, // First Blood → потом Win/Lose
        FirstBloodThenRoshanKill    // First Blood → потом RoshanKill
    }
    public class AppConfig
    {
        // ========== Twitch настройки ==========
        [JsonIgnore]
        public string AccessToken { get; set; } = "";
        [JsonProperty("EncryptedAccessToken")]
        private string EncryptedAccessToken
        {
            get => SecureStorage.Protect(AccessToken);
            set => AccessToken = SecureStorage.Unprotect(value);
        }
        public string ClientId { get; set; } = "";
        public string ChannelName { get; set; } = "";
        public string BroadcasterId { get; set; } = "";
        public string BotUsername { get; set; } = "";
        [JsonIgnore]
        public string BotAccessToken { get; set; } = "";
        [JsonProperty("EncryptedBotAccessToken")]
        private string EncryptedBotAccessToken
        {
            get => SecureStorage.Protect(BotAccessToken);
            set => BotAccessToken = SecureStorage.Unprotect(value);
        }
        public bool AutoStartChatBot { get; set; } = true;
        // ========== MMR настройки ==========
        public int CurrentMmr { get; set; } = 0;
        // ========== Dota 2 настройки ==========
        public int GSIPort { get; set; } = 2999;
        // ========== Настройки ставок ==========
        public int PredictionWindowSeconds { get; set; } = 300;
        // Один чекбокс на всю автоматику
        public bool AutomationEnabled { get; set; } = true;
        // Выбранный тип ставки
        public PredictionType SelectedPredictionType { get; set; } = PredictionType.WinLose;
        // ========== DonationAlerts ==========
        /// <summary>Client ID приложения DonationAlerts (не секрет)</summary>
        public string DonationAlertsClientId { get; set; } = "";
        [JsonIgnore]
        public string DonationAlertsClientSecret { get; set; } = "";
        [JsonProperty("EncryptedDonationAlertsClientSecret")]
        private string EncryptedDonationAlertsClientSecret
        {
            get => SecureStorage.Protect(DonationAlertsClientSecret);
            set => DonationAlertsClientSecret = SecureStorage.Unprotect(value);
        }
        [JsonIgnore]
        public string DonationAlertsAccessToken { get; set; } = "";
        [JsonProperty("EncryptedDonationAlertsAccessToken")]
        private string EncryptedDonationAlertsAccessToken
        {
            get => SecureStorage.Protect(DonationAlertsAccessToken);
            set => DonationAlertsAccessToken = SecureStorage.Unprotect(value);
        }
        [JsonIgnore]
        public string DonationAlertsRefreshToken { get; set; } = "";
        [JsonProperty("EncryptedDonationAlertsRefreshToken")]
        private string EncryptedDonationAlertsRefreshToken
        {
            get => SecureStorage.Protect(DonationAlertsRefreshToken);
            set => DonationAlertsRefreshToken = SecureStorage.Unprotect(value);
        }
        /// <summary>ID пользователя DonationAlerts — используется в имени канала $alerts:donation_&lt;id&gt;</summary>
        public string DonationAlertsBroadcasterId { get; set; } = "";
        /// <summary>Дата/время истечения access-токена DA (UTC)</summary>
        public DateTime DonationAlertsTokenExpiresAt { get; set; } = DateTime.MinValue;
        /// <summary>redirect_uri, зарегистрированный в приложении DonationAlerts</summary>
        public string DonationAlertsRedirectUri { get; set; } = "http://localhost:3000/";
        public bool DonationAlertsEnabled { get; set; } = true;
        /// <summary>Подключаться к DonationAlerts автоматически при старте приложения</summary>
        public bool AutoStartDonationAlerts { get; set; } = true;
        // ========== Музыка (!play через донат) ==========
        /// <summary>Команда в сообщении доната, запускающая трек</summary>
        public string MusicCommand { get; set; } = "!play";
        /// <summary>Минимальная сумма доната в рублях для заказа трека</summary>
        public double MinDonationAmountRub { get; set; } = 100;
        /// <summary>Путь к yt-dlp (по умолчанию ищется рядом с exe и в PATH)</summary>
        public string YtDlpPath { get; set; } = "yt-dlp.exe";
        /// <summary>Автоматически переходить к следующему треку по истечении длительности</summary>
        public bool MusicAutoAdvance { get; set; } = true;
        /// <summary>Максимальная длительность трека в секундах (0 — без ограничения)</summary>
        public int MusicMaxDurationSeconds { get; set; } = 600;
        // ========== Донатная музыка и Chrome ==========
        /// <summary>Ставить музыку стримера (YouTube-вкладка Chrome) на паузу, когда играет донатный трек</summary>
        public bool PauseStreamerMusic { get; set; } = true;
        /// <summary>Порт удалённой отладки Chrome (Chrome должен быть запущен с --remote-debugging-port=N)</summary>
        public int ChromeDebugPort { get; set; } = 9222;

        /// <summary>Автоматически пропускать рекламу YouTube во вкладках с музыкой</summary>
        public bool BlockAds { get; set; } = true;

        [JsonIgnore]
        public string ConfigPath { get; set; } = "config.json";
        // ========== Сохранение и загрузка ==========
        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
                System.Diagnostics.Debug.WriteLine($"Конфиг сохранен: {ConfigPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка сохранения: {ex.Message}");
            }
        }
        public static AppConfig Load(string path = "config.json")
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
            try
            {
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    var config = JsonConvert.DeserializeObject<AppConfig>(json);
                    if (config != null)
                    {
                        config.ConfigPath = configPath;
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки: {ex.Message}");
            }
            var defaultConfig = new AppConfig { ConfigPath = configPath };
            defaultConfig.Save();
            return defaultConfig;
        }
    }
}
