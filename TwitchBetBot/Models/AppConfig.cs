using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace TwitchBetBot.Models
{
    public class AppConfig
    {
        // ========== Twitch настройки ==========

        [JsonIgnore]
        public string AccessToken { get; set; } = "";

        [JsonProperty("EncryptedAccessToken")]
        private string EncryptedAccessToken
        {
            get => EncryptString(AccessToken);
            set => AccessToken = DecryptString(value);
        }

        public string ClientId { get; set; } = "";
        public string ChannelName { get; set; } = "";
        public string BroadcasterId { get; set; } = "";
        public string BotUsername { get; set; } = "";
        public string BotAccessToken { get; set; } = "";
        public bool AutoStartChatBot { get; set; } = true;

        // ========== MMR настройки ==========
        public int CurrentMmr { get; set; } = 0;

        // ========== Dota 2 настройки ==========
        public string[] Dota2Teams { get; set; } = { "Radiant", "Dire" };
        public int GSIPort { get; set; } = 2999;

        // ========== Настройки ставок ==========
        public int PredictionWindowSeconds { get; set; } = 300;
        public int AutoLockMinutes { get; set; } = 3;
        public int CheckIntervalSeconds { get; set; } = 30;

        // ========== Автоматизация ==========
        public bool AutoStartMonitoring { get; set; } = true;
        public bool AutoCreatePredictions { get; set; } = true;
        public bool AutoEndPredictions { get; set; } = true;
        public bool AutoLockPredictions { get; set; } = false;

        [JsonIgnore]
        public string ConfigPath { get; set; } = "config.json";

        // ========== Шифрование ==========
        private static string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return "";

            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encryptedBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Encryption error: {ex.Message}");
                return "";
            }
        }

        private static string DecryptString(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return "";

            try
            {
                byte[] encryptedBytes = Convert.FromBase64String(encryptedText);
                byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Decryption error: {ex.Message}");
                return "";
            }
        }

        // ========== Сохранение и загрузка ==========
        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
                System.Diagnostics.Debug.WriteLine($"✅ Конфиг сохранен: {ConfigPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Ошибка сохранения: {ex.Message}");
            }
        }

        public static AppConfig Load(string path = "config.json")
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);

                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    var config = JsonConvert.DeserializeObject<AppConfig>(json);
                    config.ConfigPath = configPath;
                    return config;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Ошибка загрузки: {ex.Message}");
            }

            var defaultConfig = new AppConfig { ConfigPath = path };
            defaultConfig.Save();
            return defaultConfig;
        }

        public bool TestEncryption()
        {
            try
            {
                string testString = "test_token_123";
                string encrypted = EncryptString(testString);
                string decrypted = DecryptString(encrypted);
                return testString == decrypted;
            }
            catch
            {
                return false;
            }
        }
    }
}