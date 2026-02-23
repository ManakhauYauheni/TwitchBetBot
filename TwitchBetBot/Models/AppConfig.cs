using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace TwitchBetBot.Models
{
    // Класс для хранения всех настроек программы
    // Автоматически сохраняется в config.json и загружается оттуда
    // Токен шифруется
    public class AppConfig
    {
        // ========== Twitch настройки ==========

        // Токен доступа к Twitch (не сохраняется напрямую в JSON, шифруется)
        [JsonIgnore] // Эту штуку не пишем в файл как есть
        public string AccessToken { get; set; } = "";

        // Это свойство нужно только для JSON - хранит зашифрованную версию токена
        [JsonProperty("EncryptedAccessToken")]
        private string EncryptedAccessToken
        {
            // Когда сохраняем - шифруем токен
            get => EncryptString(AccessToken);
            // Когда загружаем - расшифровываем
            set => AccessToken = DecryptString(value);
        }

        // ID приложения в Twitch (получить на dev.twitch.tv)
        public string ClientId { get; set; } = "";
        // Имя канала для ставок
        public string ChannelName { get; set; } = "";
        // ID канала (узнается автоматически при подключении)
        public string BroadcasterId { get; set; } = "";

        // ========== Dota 2 настройки ==========

        // Названия команд (Radiant и Dire)
        public string[] Dota2Teams { get; set; } = { "Radiant", "Dire" };
        // Порт для связи с Dota 2 (должен совпадать с конфигом игры)
        public int GSIPort { get; set; } = 3000;

        // ========== Настройки ставок ==========

        // Сколько секунд принимать ставки (5 минут по умолчанию)
        public int PredictionWindowSeconds { get; set; } = 300;
        // Через сколько минут автоматически закрывать прием ставок
        public int AutoLockMinutes { get; set; } = 3;
        // Как часто проверять статус ставок (в секундах)
        public int CheckIntervalSeconds { get; set; } = 30;

        // ========== Автоматизация ==========

        // Запускать ли мониторинг сразу после старта программы
        public bool AutoStartMonitoring { get; set; } = true;
        // Автоматически создавать ставки при старте игры
        public bool AutoCreatePredictions { get; set; } = true;
        // Автоматически завершать ставки по окончании игры
        public bool AutoEndPredictions { get; set; } = true;
        // Автоматически закрывать прием ставок через AutoLockMinutes минут
        public bool AutoLockPredictions { get; set; } = false;

        // Путь к файлу конфигурации
        [JsonIgnore]
        public string ConfigPath { get; set; } = "config.json";

        // ========== Методы шифрования ==========

        // Шифрует строку так, что только текущий пользователь Windows может её расшифровать
        private static string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return "";

            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                // Используем встроенную защиту Windows (DPAPI)
                // DataProtectionScope.CurrentUser - только этот пользователь сможет расшифровать
                byte[] encryptedBytes = ProtectedData.Protect(plainBytes,
                    null, 
                    DataProtectionScope.CurrentUser);

                return Convert.ToBase64String(encryptedBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Encryption error: {ex.Message}");
                return "";
            }
        }

        // Расшифровывает строку (обратная операция)
        private static string DecryptString(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return "";

            try
            {
                byte[] encryptedBytes = Convert.FromBase64String(encryptedText);

                byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes,
                    null,
                    DataProtectionScope.CurrentUser);

                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Decryption error: {ex.Message}");
                return "";
            }
        }

        // ========== Сохранение и загрузка ==========

        // Сохраняет настройки в файл
        public void Save()
        {
            try
            {
                // Превращаем объект в JSON строку с отступами для красоты
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
                System.Diagnostics.Debug.WriteLine($"✅ Конфиг сохранен (зашифрован): {ConfigPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Ошибка сохранения конфига: {ex.Message}");
            }
        }

        // Загружает настройки из файла (если файла нет - создает новый)
        public static AppConfig Load(string path = "config.json")
        {
            try
            {
                // Получаем полный путь к файлу (где лежит программа)
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);

                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    var config = JsonConvert.DeserializeObject<AppConfig>(json);
                    config.ConfigPath = configPath;
                    System.Diagnostics.Debug.WriteLine($"✅ Конфиг загружен (расшифрован): {configPath}");
                    return config;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Ошибка загрузки конфига: {ex.Message}");
            }

            // Если файла нет или ошибка - создаем новый конфиг с настройками по умолчанию
            var defaultConfig = new AppConfig { ConfigPath = path };
            defaultConfig.Save(); // Сразу сохраняем, чтобы файл появился
            return defaultConfig;
        }

        // Проверка что шифрование работает (для отладки)
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