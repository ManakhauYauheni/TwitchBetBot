using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TwitchBetBot.Models;
using TwitchBetBot.Services;
using TwitchBetBot.Utils;

namespace TwitchBetBot.ViewModels
{
    // Главная ViewModel - связывает интерфейс с логикой
    // Содержит все данные, которые отображаются в окне, и команды для кнопок
    public class MainViewModel : ViewModelBase
    {
        // ========== Приватные поля ==========

        private readonly AppConfig _config;                    // Настройки
        private readonly TwitchAuthService _authService;        // Для авторизации
        private readonly PredictionService _predictionService;  // Для ставок
        private readonly Dota2GameService _gameService;         // Для Dota 2
        private System.Timers.Timer _monitoringTimer;           // Таймер проверки статуса
        private System.Timers.Timer _logCleanupTimer;           // Таймер для очистки логов

        // Для автоочистки логов
        private const int MAX_LOG_LINES = 1000;                  // Сколько строк максимум храним
        private System.Collections.Generic.List<string> _logLines = new System.Collections.Generic.List<string>();

        // Свойства, которые отображаются в интерфейсе
        private string _logText = "";
        private bool _isConnected = false;
        private bool _isMonitoring = false;
        private bool _isGameRunning = false;
        private Prediction _currentPrediction;
        private Dota2Match _currentMatch;

        // ========== Свойства для привязки в XAML ==========

        public string LogText
        {
            get => _logText;
            set => SetProperty(ref _logText, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value);
        }

        public bool IsMonitoring
        {
            get => _isMonitoring;
            set => SetProperty(ref _isMonitoring, value);
        }

        public bool IsGameRunning
        {
            get => _isGameRunning;
            set => SetProperty(ref _isGameRunning, value);
        }

        public Prediction CurrentPrediction
        {
            get => _currentPrediction;
            set => SetProperty(ref _currentPrediction, value);
        }

        public Dota2Match CurrentMatch
        {
            get => _currentMatch;
            set => SetProperty(ref _currentMatch, value);
        }

        // Данные из полей ввода
        public string AccessToken { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string ChannelName { get; set; } = "";

        // ========== Команды для кнопок ==========

        public ICommand ConnectCommand { get; }               // Подключиться к Twitch
        public ICommand ToggleMonitoringCommand { get; }       // Вкл/выкл мониторинг
        public ICommand StartGSICommand { get; }               // Запустить GSI
        public ICommand StopGSICommand { get; }                // Остановить GSI
        public ICommand CreatePredictionCommand { get; }       // Создать ставку вручную
        public ICommand LockPredictionCommand { get; }         // Закрыть прием ставок
        public ICommand EndPredictionRadiantCommand { get; }   // Завершить победой Radiant
        public ICommand EndPredictionDireCommand { get; }      // Завершить победой Dire
        public ICommand CancelPredictionCommand { get; }       // Отменить ставку
        public ICommand SaveConfigCommand { get; }             // Сохранить настройки
        public ICommand TestPredictionCommand { get; }         // Тестовая ставка
        public ICommand TestEncryptionCommand { get; }         // Проверить шифрование
                                                               // ========== Конструктор ==========

        public MainViewModel()
        {
            // Загружаем настройки из файла
            _config = AppConfig.Load();
            LoadConfigFromModel();

            // Создаем сервисы
            _authService = new TwitchAuthService();
            _predictionService = new PredictionService(_config);
            _gameService = new Dota2GameService(_config, this);

            // Подписываемся на события от сервисов
            _predictionService.OnPredictionCreated += OnPredictionCreated;
            _predictionService.OnPredictionUpdated += OnPredictionUpdated;
            _predictionService.OnPredictionEnded += OnPredictionEnded;
            _gameService.OnGameStarted += OnGSIGameStarted;
            _gameService.OnGameEnded += OnGSIGameEnded;

            // Создаем команды для кнопок
            ConnectCommand = new RelayCommand(() => _ = ConnectToTwitchAsync());
            ToggleMonitoringCommand = new RelayCommand(ToggleMonitoring);
            StartGSICommand = new RelayCommand(StartGSI);
            StopGSICommand = new RelayCommand(StopGSI);
            CreatePredictionCommand = new RelayCommand(() => _ = CreatePredictionAsync());
            LockPredictionCommand = new RelayCommand(() => _ = LockPredictionAsync());
            EndPredictionRadiantCommand = new RelayCommand(() => _ = EndPredictionAsync("Radiant"));
            EndPredictionDireCommand = new RelayCommand(() => _ = EndPredictionAsync("Dire"));
            CancelPredictionCommand = new RelayCommand(() => _ = CancelPredictionAsync());
            SaveConfigCommand = new RelayCommand(SaveConfig);
            TestPredictionCommand = new RelayCommand(() => _ = TestPredictionAsync());
            TestEncryptionCommand = new RelayCommand(TestEncryption_Click);

            // Таймер для проверки статуса ставок (каждые N секунд)
            _monitoringTimer = new System.Timers.Timer(_config.CheckIntervalSeconds * 1000);
            _monitoringTimer.Elapsed += async (s, e) =>
            {
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await CheckGameStatusAsync();
                });
            };
            _monitoringTimer.AutoReset = true;

            // Таймер для очистки логов (каждые 5 минут)
            _logCleanupTimer = new System.Timers.Timer(300000); // 300000 мс = 5 минут
            _logCleanupTimer.Elapsed += (s, e) =>
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    CleanupOldLogs();
                });
            };
            _logCleanupTimer.AutoReset = true;
            _logCleanupTimer.Start();

            // Приветственное сообщение
            Log("🚀 Twitch Bet Bot для Dota 2 запущен");
            Log("1. Введите данные авторизации Twitch");
            Log("2. Нажмите 'Подключиться'");
            Log("3. Нажмите 'Запустить мониторинг'");
            Log("4. Запустите Dota 2 и начните игру");
            Log("🔄 Логи будут автоматически очищаться каждые 5 минут");

            // Если включен автостарт - пробуем подключиться через 3 секунды
            if (_config.AutoStartMonitoring)
            {
                Task.Delay(3000).ContinueWith(async _ =>
                {
                    await Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        var connected = await ConnectToTwitchAsync();
                        if (connected)
                        {
                            StartMonitoring();
                        }
                    });
                });
            }
        }

        // ========== Работа с конфигом ==========

        private void LoadConfigFromModel()
        {
            AccessToken = _config.AccessToken;
            ClientId = _config.ClientId;
            ChannelName = _config.ChannelName;
        }

        private void SaveConfigToModel()
        {
            _config.AccessToken = AccessToken;
            _config.ClientId = ClientId;
            _config.ChannelName = ChannelName;
        }

        // Сохранение настроек
        private void SaveConfig()
        {
            SaveConfigToModel();
            Log($"💾 Сохранение конфига...");
            Log($"   AccessToken: {(string.IsNullOrEmpty(AccessToken) ? "❌ ПУСТ" : "✅ ЗАШИФРОВАН")}");
            Log($"   ClientId: {(string.IsNullOrEmpty(ClientId) ? "❌ ПУСТ" : "✅ " + ClientId)}");
            Log($"   ChannelName: {(string.IsNullOrEmpty(ChannelName) ? "❌ ПУСТ" : "✅ " + ChannelName)}");

            _config.Save();

            if (File.Exists(_config.ConfigPath))
            {
                var fileInfo = new FileInfo(_config.ConfigPath);
                Log($"📄 Файл config.json создан (размер: {fileInfo.Length} байт)");

                if (System.Diagnostics.Debugger.IsAttached)
                {
                    var content = File.ReadAllText(_config.ConfigPath);
                    Log($"🔍 Первые 50 символов: {content.Substring(0, Math.Min(50, content.Length))}...");
                }
            }

            Log("💾 Конфиг сохранен (токен зашифрован)");
        }

        // Очистка старой ставки из кэша
        private void ClearOldPrediction()
        {
            CurrentPrediction = null;

            try
            {
                // Рефлексией лезем в приватное поле _currentPrediction в PredictionService
                var serviceType = _predictionService.GetType();
                var field = serviceType.GetField("_currentPrediction",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (field != null)
                {
                    field.SetValue(_predictionService, null);
                    Log("🧹 Очищены данные о старой ставке");
                }
            }
            catch (Exception ex)
            {
                Log($"⚠️ Не удалось очистить состояние: {ex.Message}");
            }
        }
        // ========== Подключение к Twitch ==========

        private async Task<bool> ConnectToTwitchAsync()
        {
            try
            {
                Log("🔐 Проверка подключения к Twitch...");

                // Проверяем что все поля заполнены
                if (string.IsNullOrEmpty(AccessToken))
                {
                    Log("❌ Access Token не заполнен");
                    return false;
                }
                ClearOldPrediction();

                if (string.IsNullOrEmpty(ClientId))
                {
                    Log("❌ Client ID не заполнен");
                    Log("ℹ️ Получите Client ID на: https://dev.twitch.tv/console");
                    return false;
                }

                if (string.IsNullOrEmpty(ChannelName))
                {
                    Log("❌ Имя канала не заполнено");
                    return false;
                }

                // Сохраняем введенные данные
                SaveConfigToModel();
                _config.Save();

                // Проверяем токен
                var validation = await _authService.ValidateToken(_config.AccessToken);
                if (validation == null)
                {
                    Log("❌ Неверный Access Token");
                    return false;
                }

                Log($"✅ Токен валиден: {validation.Login}");

                // Получаем ID канала
                _config.BroadcasterId = await _authService.GetBroadcasterId(
                    _config.AccessToken, _config.ClientId, _config.ChannelName);

                if (string.IsNullOrEmpty(_config.BroadcasterId))
                {
                    Log("❌ Не удалось получить ID канала");
                    return false;
                }

                Log($"📺 Канал: {_config.ChannelName} (ID: {_config.BroadcasterId})");

                // Проверяем, есть ли уже активная ставка
                var current = await _predictionService.GetCurrentPredictionAsync();
                if (current != null)
                {
                    CurrentPrediction = current;
                    Log($"📊 Найдена активная ставка: {current.Title}");
                }

                IsConnected = true;
                Log("✅ Подключено к Twitch!");
                Log("🎮 Теперь можете запустить мониторинг");

                return true;
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка подключения: {ex.Message}");
                return false;
            }
        }

        // ========== Мониторинг ==========

        private void ToggleMonitoring()
        {
            if (!IsConnected)
            {
                Log("⚠️ Сначала подключитесь к Twitch");
                return;
            }

            if (IsMonitoring)
            {
                StopMonitoring();
            }
            else
            {
                StartMonitoring();
            }
        }

        private void StartMonitoring()
        {
            if (!IsConnected)
            {
                Log("⚠️ Сначала подключитесь к Twitch");
                return;
            }

            _monitoringTimer.Start();
            IsMonitoring = true;
            Log("🔍 Мониторинг запущен");
            Log($"⏱️ Проверка каждые {_config.CheckIntervalSeconds} секунд");

            StartGSI();
        }

        private void StopMonitoring()
        {
            _monitoringTimer.Stop();
            IsMonitoring = false;
            Log("🛑 Мониторинг остановлен");
            StopGSI();
        }

        // Проверка статуса ставки (вызывается по таймеру)
        private async Task CheckGameStatusAsync()
        {
            if (!IsMonitoring || !IsConnected) return;

            try
            {
                var currentPrediction = await _predictionService.GetCurrentPredictionAsync();
                if (currentPrediction != null)
                {
                    CurrentPrediction = currentPrediction;

                    // Автоматическое закрытие ставки через AutoLockMinutes минут
                    if (_config.AutoLockPredictions && currentPrediction.Status == PredictionStatus.ACTIVE)
                    {
                        var timeActive = DateTime.Now - currentPrediction.CreatedAt;
                        if (timeActive.TotalMinutes >= _config.AutoLockMinutes)
                        {
                            Log($"⏰ Авто-закрытие приема ставок (прошло {_config.AutoLockMinutes} мин)");
                            await _predictionService.LockPredictionAsync();
                        }
                    }
                }
                else
                {
                    CurrentPrediction = null;
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка проверки статуса: {ex.Message}");
            }
        }

        // ========== Управление GSI ==========

        private void StartGSI()
        {
            if (!IsMonitoring)
            {
                Log("⚠️ Сначала запустите мониторинг");
                return;
            }

            try
            {
                _gameService.Start();
                IsGameRunning = _gameService.IsGameRunning();
                Log("🎮 Dota2 GSI запущен");
                Log("ℹ️ Запустите Dota 2 и начните игру");
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка запуска GSI: {ex.Message}");
            }
        }

        private void StopGSI()
        {
            _gameService.Stop();
            IsGameRunning = false;
            Log("🛑 GSI остановлен");
        }
        // ========== Обработка событий от Dota 2 ==========

        // Игра началась
        private async void OnGSIGameStarted(object sender, Dota2Match match)
        {
            try
            {
                if (match == null)
                {
                    Log("❌ OnGSIGameStarted: match is null");
                    return;
                }

                CurrentMatch = match;
                IsGameRunning = true;
                Log($"🎮 ИГРА НАЧАЛАСЬ!");

                // Если включено авто-создание ставок
                if (_config.AutoCreatePredictions && IsConnected)
                {
                    Log("🔄 Принудительное обновление данных Twitch...");
                    await _predictionService.ForceRefresh();
                    await Task.Delay(2000);

                    CurrentPrediction = null;
                    await TryCreatePrediction(match);
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка в OnGSIGameStarted: {ex.Message}");
            }
        }

        // Попытка создать ставку
        private async Task TryCreatePrediction(Dota2Match match)
        {
            try
            {
                Log("🎲 Попытка создать ставку...");

                var activePrediction = await _predictionService.GetCurrentPredictionAsync();

                if (activePrediction == null)
                {
                    Log("✅ Нет активных ставок, создаём новую");
                    await CreatePredictionForMatch(match);
                }
                else
                {
                    Log($"⚠️ Найдена ставка: {activePrediction.Title}");
                    Log($"   Статус: {activePrediction.Status}, ID: {activePrediction.Id}");

                    // Если ставка завершена - создаем новую
                    if (activePrediction.Status == PredictionStatus.RESOLVED ||
                        activePrediction.Status == PredictionStatus.CANCELED)
                    {
                        Log("🔄 Старая ставка завершена, создаём новую");
                        await CreatePredictionForMatch(match);
                    }
                    else
                    {
                        Log("⏸️ Активная ставка уже существует, пропускаем");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка TryCreatePrediction: {ex.Message}");
            }
        }

        // Создание ставки для конкретного матча
        private async Task CreatePredictionForMatch(Dota2Match match)
        {
            try
            {
                if (match == null)
                {
                    Log("❌ CreatePredictionForMatch: match is null");
                    return;
                }

                var title = $"Dota 2: {match.RadiantTeam} vs {match.DireTeam} - Кто победит?";
                var outcomes = new[] { match.RadiantTeam, match.DireTeam };

                Log($"🎲 Авто-создание ставки: {title}");

                var prediction = await _predictionService.CreatePredictionAsync(
                    title, outcomes, _config.PredictionWindowSeconds);

                if (prediction != null)
                {
                    CurrentPrediction = prediction;
                    Log($"✅ Авто-ставка создана!");
                    Log($"⏱️ Прием прогнозов: {_config.PredictionWindowSeconds} секунд");
                    Log($"⏰ Авто-закрытие через: {_config.AutoLockMinutes} минут");
                }
                else
                {
                    Log("❌ Не удалось создать авто-ставку");
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка создания авто-ставки: {ex.Message}");
            }
        }

        // Игра закончилась
        private async void OnGSIGameEnded(object sender, Dota2Match match)
        {
            try
            {
                IsGameRunning = false;

                // СЛУЧАЙ 1: Дисконнект/лив
                if (match == null)
                {
                    Log("ℹ️ Получен сигнал об окончании игры (дисконнект)");

                    if (_config.AutoEndPredictions && CurrentPrediction != null)
                    {
                        Log("🚫 Дисконнект - отменяем ставку...");
                        await CancelPredictionOnDisconnect();
                    }

                    CurrentMatch = null;
                    await CleanupAfterGame();
                    return;
                }

                // СЛУЧАЙ 2: Явная отмена (CANCELED)
                if (match.Winner == "CANCELED" || match.Status == MatchStatus.Canceled)
                {
                    Log($"🚫 МАТЧ ОТМЕНЕН из-за дисконнекта!");

                    if (_config.AutoEndPredictions && CurrentPrediction != null)
                    {
                        await CancelPredictionOnDisconnect();
                    }

                    CurrentMatch = null;
                    await CleanupAfterGame();
                    return;
                }

                // СЛУЧАЙ 3: Нормальное завершение с победителем
                Log($"🏁 ИГРА ЗАВЕРШЕНА!");

                if (match.Duration != null)
                {
                    Log($"⏱️ Длительность: {match.Duration:mm\\:ss}");
                }

                if (!string.IsNullOrEmpty(match.Winner))
                {
                    Log($"🏆 Победитель: {match.Winner}");
                }

                CurrentMatch = null;

                if (_config.AutoEndPredictions && CurrentPrediction != null)
                {
                    await Task.Delay(5000); // Даем время на обработку
                    await EndPredictionForMatch(match);
                }

                await CleanupAfterGame();
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка в OnGSIGameEnded: {ex.Message}");
            }
        }

        // Завершение ставки по результатам матча
        private async Task EndPredictionForMatch(Dota2Match match)
        {
            try
            {
                if (CurrentPrediction == null)
                {
                    Log("⚠️ Нет активной ставки для завершения");
                    return;
                }

                if (match == null)
                {
                    Log("❌ EndPredictionForMatch: match is null");
                    return;
                }

                if (!IsConnected)
                {
                    Log("⚠️ Нет подключения к Twitch, не могу завершить ставку");
                    return;
                }

                if (match.Winner == "CANCELED" || match.Status == MatchStatus.Canceled)
                {
                    Log("⚠️ Матч отменен, пропускаем завершение ставки");
                    return;
                }

                if (string.IsNullOrEmpty(match.Winner))
                {
                    Log("⚠️ Не определен победитель для завершения ставки");
                    return;
                }

                Log($"🏆 Авто-завершение ставки в пользу: {match.Winner}");

                // Ищем ID победившего исхода
                string winningOutcomeId = null;
                foreach (var outcome in CurrentPrediction.Outcomes)
                {
                    if (outcome.Title.Contains(match.Winner, StringComparison.OrdinalIgnoreCase))
                    {
                        winningOutcomeId = outcome.Id;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(winningOutcomeId))
                {
                    winningOutcomeId = CurrentPrediction.Outcomes[0].Id;
                    Log($"⚠️ Не найден outcome для {match.Winner}, использую первый");
                }

                var success = await _predictionService.EndPredictionAsync(winningOutcomeId);

                if (success)
                {
                    Log($"✅ Ставка авто-завершена в пользу {match.Winner}!");
                    CurrentPrediction = null;
                }
                else
                {
                    Log("❌ Не удалось авто-завершить ставку");
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка авто-завершения ставки: {ex.Message}");
            }
        }
        // ========== Отмена ставки при дисконнекте ==========

        private async Task CancelPredictionOnDisconnect()
        {
            try
            {
                if (CurrentPrediction == null)
                {
                    Log("⚠️ Нет активной ставки для отмены");
                    return;
                }

                if (!IsConnected)
                {
                    Log("⚠️ Нет подключения к Twitch, пробую восстановить...");
                    var connected = await ConnectToTwitchAsync();

                    if (!connected)
                    {
                        Log("❌ Не удалось подключиться к Twitch, ставка не отменена");
                        return;
                    }
                }

                Log($"🚫 Отмена ставки: {CurrentPrediction.Title}");

                var success = await _predictionService.CancelPredictionAsync();

                if (success)
                {
                    Log($"✅ Ставка успешно отменена! Баллы возвращены зрителям.");
                    CurrentPrediction = null;
                }
                else
                {
                    Log("❌ Не удалось отменить ставку через API");
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка отмены ставки: {ex.Message}");
            }
        }

        // ========== Очистка памяти после игры ==========

        private async Task CleanupAfterGame()
        {
            try
            {
                Log("🧹 Очистка после игры...");
                await Task.Delay(1000);

                // Принудительно вызываем сборщик мусора
                GC.Collect();
                GC.WaitForPendingFinalizers();

                var memoryMB = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024;
                Log($"📊 Память после очистки: {memoryMB} МБ");
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка при очистке памяти: {ex.Message}");
            }
        }

        // ========== Автоочистка логов ==========

        private void CleanupOldLogs()
        {
            try
            {
                if (_logLines.Count > MAX_LOG_LINES)
                {
                    int beforeCount = _logLines.Count;
                    int removedCount = beforeCount - MAX_LOG_LINES;

                    _logLines.RemoveRange(0, removedCount);
                    LogText = string.Join("\n", _logLines);
                    OnPropertyChanged(nameof(LogText));

                    var logMessage = $"[{DateTime.Now:HH:mm:ss}] 🧹 Автоочистка: удалено {removedCount} старых строк (всего было {beforeCount})";
                    _logLines.Add(logMessage);
                    LogText = string.Join("\n", _logLines);
                    OnPropertyChanged(nameof(LogText));

                    System.Diagnostics.Debug.WriteLine(logMessage);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Логов мало: {_logLines.Count}/{MAX_LOG_LINES}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при очистке логов: {ex.Message}");
            }
        }

        // ========== Ручные операции со ставками ==========

        // Тестовая ставка
        private async Task TestPredictionAsync()
        {
            if (!IsConnected)
            {
                Log("⚠️ Сначала подключитесь к Twitch");
                return;
            }

            try
            {
                Log("🧪 Тестовая ставка...");
                var title = "Тестовая ставка - Кто победит?";
                var outcomes = new[] { "Radiant", "Dire" };

                var prediction = await _predictionService.CreatePredictionAsync(
                    title, outcomes, 120); // 2 минуты

                if (prediction != null)
                {
                    CurrentPrediction = prediction;
                    Log("✅ Тестовая ставка создана!");
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка теста: {ex.Message}");
            }
        }

        // Создать ставку вручную
        private async Task CreatePredictionAsync()
        {
            if (!IsConnected)
            {
                Log("⚠️ Сначала подключитесь к Twitch");
                return;
            }

            try
            {
                var title = $"Dota 2: Radiant vs Dire - Кто победит?";
                var outcomes = new[] { "Radiant", "Dire" };

                Log($"🎲 Создание ставки: {title}");

                var prediction = await _predictionService.CreatePredictionAsync(
                    title, outcomes, _config.PredictionWindowSeconds);

                if (prediction != null)
                {
                    CurrentPrediction = prediction;
                    Log($"✅ Ставка создана!");
                    Log($"⏱️ Прием прогнозов: {_config.PredictionWindowSeconds} секунд");
                }
                else
                {
                    Log("❌ Не удалось создать ставку");
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка: {ex.Message}");
            }
        }

        // Закрыть прием ставок
        private async Task LockPredictionAsync()
        {
            if (CurrentPrediction == null)
            {
                Log("⚠️ Нет активной ставки");
                return;
            }

            if (!IsConnected)
            {
                Log("⚠️ Нет подключения к Twitch");
                return;
            }

            try
            {
                Log("🔒 Закрытие приема прогнозов...");
                var success = await _predictionService.LockPredictionAsync();

                if (success)
                    Log("✅ Прием прогнозов закрыт");
                else
                    Log("❌ Не удалось закрыть ставку");
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка: {ex.Message}");
            }
        }

        // Завершить ставку с победителем
        private async Task EndPredictionAsync(string winner)
        {
            if (CurrentPrediction == null)
            {
                Log("⚠️ Нет активной ставки");
                return;
            }

            if (!IsConnected)
            {
                Log("⚠️ Нет подключения к Twitch");
                return;
            }

            try
            {
                Log($"🏆 Завершение в пользу: {winner}");

                string winningOutcomeId = "";
                foreach (var outcome in CurrentPrediction.Outcomes)
                {
                    if (outcome.Title.Contains(winner, StringComparison.OrdinalIgnoreCase))
                    {
                        winningOutcomeId = outcome.Id;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(winningOutcomeId))
                {
                    Log($"❌ Не найден outcome для: {winner}");
                    return;
                }

                var success = await _predictionService.EndPredictionAsync(winningOutcomeId);

                if (success)
                {
                    Log($"✅ Ставка завершена в пользу {winner}!");
                    CurrentPrediction = null;
                }
                else
                {
                    Log("❌ Не удалось завершить ставку");
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка: {ex.Message}");
            }
        }

        // Отменить ставку
        private async Task CancelPredictionAsync()
        {
            if (CurrentPrediction == null)
            {
                Log("⚠️ Нет активной ставки");
                return;
            }

            if (!IsConnected)
            {
                Log("⚠️ Нет подключения к Twitch");
                return;
            }

            try
            {
                Log("❌ Отмена ставки...");
                var success = await _predictionService.CancelPredictionAsync();

                if (success)
                {
                    Log("✅ Ставка отменена");
                    CurrentPrediction = null;
                }
                else
                {
                    Log("❌ Не удалось отменить ставку");
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка: {ex.Message}");
            }
        }
        // ========== Обработка событий от PredictionService ==========

        private void OnPredictionCreated(object sender, Prediction prediction)
        {
            Log($"🎲 Создана ставка: {prediction.Title}");
        }

        private void OnPredictionUpdated(object sender, Prediction prediction)
        {
            Log($"📊 Статус обновлен: {prediction.Status}");
        }

        private void OnPredictionEnded(object sender, Prediction prediction)
        {
            Log($"🏁 Ставка завершена: {prediction.Status}");
            CurrentPrediction = null;
        }

        // ========== Проверка шифрования ==========

        private void TestEncryption_Click()
        {
            bool encryptionWorks = _config.TestEncryption();
            if (encryptionWorks)
            {
                Log("✅ Шифрование работает корректно");
            }
            else
            {
                Log("❌ Ошибка шифрования! Проверьте, установлен ли NuGet пакет System.Security.Cryptography.ProtectedData");
            }
        }

        // ========== Логирование ==========

        public void Log(string message, bool showTimestamp = true)
        {
            try
            {
                var timestamp = showTimestamp ? $"[{DateTime.Now:HH:mm:ss}] " : "";
                var line = timestamp + message;

                // Добавляем в список
                _logLines.Add(line);

                // Если вдруг список слишком большой - чистим
                if (_logLines.Count > MAX_LOG_LINES + 100)
                {
                    int removedCount = _logLines.Count - MAX_LOG_LINES;
                    _logLines.RemoveRange(0, removedCount);
                }

                // Обновляем текст
                LogText = string.Join("\n", _logLines);
                OnPropertyChanged(nameof(LogText));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка логирования: {ex.Message}");
            }
        }
    }
}