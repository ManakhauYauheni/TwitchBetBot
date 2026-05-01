using Dota2GSI;
using Dota2GSI.Nodes;
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
    public enum AppMode
    {
        Tracker,    // Только трекер MMR (без Twitch)
        Full        // Полный режим (Twitch + ставки + чат)
    }

    public class MainViewModel : ViewModelBase
    {
        // ========== Приватные поля ==========

        private readonly AppConfig _config;
        private readonly TwitchAuthService _authService;
        private readonly PredictionService _predictionService;
        private readonly Dota2GameService _gameService;
        private System.Timers.Timer _monitoringTimer;
        private System.Timers.Timer _logCleanupTimer;

        private TwitchChatService _chatService;
        private SessionStats _sessionStats;
        private OpenDotaService _openDotaService;

        private const int MAX_LOG_LINES = 1000;
        private System.Collections.Generic.List<string> _logLines = new System.Collections.Generic.List<string>();

        private string _logText = "";
        private bool _isConnected = false;
        private bool _isMonitoring = false;
        private bool _isGameRunning = false;
        private Prediction _currentPrediction;
        private Dota2Match _currentMatch;


        private AppMode _currentMode = AppMode.Tracker;

        // ========== Свойства ==========

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

        public string AccessToken { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string ChannelName { get; set; } = "";
        public string BroadcasterId { get; set; } = "";
        public string BotUsername { get; set; } = "";
        public string BotAccessToken { get; set; } = "";
        private bool _isChatBotRunning = false;
        public bool IsChatBotRunning
        {
            get => _isChatBotRunning;
            set => SetProperty(ref _isChatBotRunning, value);
        }

        // ========== Команды ==========

        public ICommand ConnectCommand { get; }
        public ICommand ToggleMonitoringCommand { get; }
        public ICommand StartGSICommand { get; }
        public ICommand StopGSICommand { get; }
        public ICommand CreatePredictionCommand { get; }
        public ICommand LockPredictionCommand { get; }
        public ICommand EndPredictionWinCommand { get; }
        public ICommand EndPredictionLoseCommand { get; }
        public ICommand CancelPredictionCommand { get; }
        public ICommand SaveConfigCommand { get; }
        public ICommand TestPredictionCommand { get; }
        public ICommand TestEncryptionCommand { get; }
        public ICommand StartChatBotCommand { get; }
        public ICommand StopChatBotCommand { get; }

        // ========== Конструктор ==========

        public MainViewModel()
        {
            _config = AppConfig.Load();
            LoadConfigFromModel();

            _authService = new TwitchAuthService();
            _predictionService = new PredictionService(_config);

            // ========== OpenDota и GameService ==========
            _openDotaService = new OpenDotaService((msg) => Log(msg));
            _gameService = new Dota2GameService(_config, this, _openDotaService);
            // ============================================

            _sessionStats = new SessionStats();
            _gameService.SessionStats = _sessionStats;

            if (_config.CurrentMmr > 0)
            {
                _sessionStats.SetMmr(_config.CurrentMmr);
                Log($"📊 MMR загружен из конфига: {_config.CurrentMmr}");
            }

            _predictionService.OnPredictionCreated += OnPredictionCreated;
            _predictionService.OnPredictionUpdated += OnPredictionUpdated;
            _predictionService.OnPredictionEnded += OnPredictionEnded;
            _gameService.OnGameStarted += OnGSIGameStarted;
            _gameService.OnGameEnded += OnGSIGameEnded;

            ConnectCommand = new RelayCommand(() => _ = ConnectToTwitchAsync());
            ToggleMonitoringCommand = new RelayCommand(ToggleMonitoring);
            StartGSICommand = new RelayCommand(StartGSI);
            StopGSICommand = new RelayCommand(StopGSI);
            CreatePredictionCommand = new RelayCommand(() => _ = CreatePredictionAsync());
            LockPredictionCommand = new RelayCommand(() => _ = LockPredictionAsync());
            EndPredictionWinCommand = new RelayCommand(() => _ = EndPredictionAsync("Win"));
            EndPredictionLoseCommand = new RelayCommand(() => _ = EndPredictionAsync("Lose"));
            CancelPredictionCommand = new RelayCommand(() => _ = CancelPredictionAsync());
            SaveConfigCommand = new RelayCommand(SaveConfig);   
            TestEncryptionCommand = new RelayCommand(TestEncryption_Click);
            StartChatBotCommand = new RelayCommand(StartChatBot);
            StopChatBotCommand = new RelayCommand(StopChatBot);

            _monitoringTimer = new System.Timers.Timer(_config.CheckIntervalSeconds * 1000);
            _monitoringTimer.Elapsed += async (s, e) =>
            {
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await CheckGameStatusAsync();
                });
            };
            _monitoringTimer.AutoReset = true;

            _logCleanupTimer = new System.Timers.Timer(300000);
            _logCleanupTimer.Elapsed += (s, e) =>
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    CleanupOldLogs();
                });
            };
            _logCleanupTimer.AutoReset = true;
            _logCleanupTimer.Start();

            Log("🚀 Twitch Bet Bot для Dota 2 запущен");
            Log("🎯 Режим: Трекер (только MMR)");
            Log("💡 Для переключения в полный режим используйте переключатель вверху");
            Log("🔄 Логи будут автоматически очищаться каждые 5 минут");

            StartGSI();
        }

        // ========== Работа с конфигом ==========

        private void LoadConfigFromModel()
        {
            AccessToken = _config.AccessToken;
            ClientId = _config.ClientId;
            ChannelName = _config.ChannelName;
            BroadcasterId = _config.BroadcasterId;
            BotUsername = _config.BotUsername;
            BotAccessToken = _config.BotAccessToken;
        }

        private void SaveConfigToModel()
        {
            _config.AccessToken = AccessToken;
            _config.ClientId = ClientId;
            _config.ChannelName = ChannelName;
            _config.BroadcasterId = BroadcasterId;
            _config.BotUsername = BotUsername;
            _config.BotAccessToken = BotAccessToken;
            _config.CurrentMmr = _sessionStats.CurrentMmr;
        }

        private void SaveConfig()
        {
            SaveConfigToModel();
            Log($"💾 Сохранение конфига...");
            Log($"   AccessToken: {(string.IsNullOrEmpty(AccessToken) ? "❌ ПУСТ" : "✅ ЗАШИФРОВАН")}");
            Log($"   ClientId: {(string.IsNullOrEmpty(ClientId) ? "❌ ПУСТ" : "✅ " + ClientId)}");
            Log($"   ChannelName: {(string.IsNullOrEmpty(ChannelName) ? "❌ ПУСТ" : "✅ " + ChannelName)}");
            Log($"   BotUsername: {(string.IsNullOrEmpty(BotUsername) ? "❌ ПУСТ" : "✅ " + BotUsername)}");
            Log($"   CurrentMmr: {_sessionStats.CurrentMmr}");

            _config.Save();

            if (File.Exists(_config.ConfigPath))
            {
                var fileInfo = new FileInfo(_config.ConfigPath);
                Log($"📄 Файл config.json обновлён (размер: {fileInfo.Length} байт)");
            }

            Log("💾 Конфиг сохранен (токен зашифрован)");
        }

        private void ClearOldPrediction()
        {
            CurrentPrediction = null;
            try
            {
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

        // ========== Переключение режимов ==========

        public void SwitchToFullMode()
        {
            if (_currentMode == AppMode.Full) return;

            _currentMode = AppMode.Full;
            Log("🌊 Переключено в полный режим (с Twitch)");

            


            if (!IsConnected && !string.IsNullOrEmpty(AccessToken) && !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ChannelName))
            {
                _ = ConnectToTwitchAsync();
            }

           


        }

        public void SwitchToTrackerMode()
        {
            if (_currentMode == AppMode.Tracker) return;

            _currentMode = AppMode.Tracker;
            Log("🎯 Переключено в режим трекера (только MMR)");

            if (IsChatBotRunning)
            {
                StopChatBot();
            }
        }

        // ========== Подключение к Twitch ==========

        private async Task<bool> ConnectToTwitchAsync()
        {
            try
            {
                Log("🔐 Проверка подключения к Twitch...");

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

                var validation = await _authService.ValidateToken(AccessToken);
                if (validation == null)
                {
                    Log("❌ Неверный Access Token");
                    return false;
                }

                Log($"✅ Токен валиден: {validation.Login}");

                _config.BroadcasterId = await _authService.GetBroadcasterId(
                    AccessToken, ClientId, ChannelName);

                if (string.IsNullOrEmpty(_config.BroadcasterId))
                {
                    Log("❌ Не удалось получить ID канала");
                    return false;
                }

                BroadcasterId = _config.BroadcasterId;
                Log($"📺 Канал: {ChannelName} (ID: {BroadcasterId})");

                SaveConfigToModel();
                _config.Save();

                var current = await _predictionService.GetCurrentPredictionAsync();
                if (current != null)
                {
                    CurrentPrediction = current;
                    Log($"📊 Найдена активная ставка: {current.Title}");
                }

                IsConnected = true;
                Log("✅ Подключено к Twitch!");
                Log("🎮 Теперь можете запустить мониторинг");



                if (_currentMode == AppMode.Full && !IsMonitoring)
                {
                    StartMonitoring();
                    StartChatBot();
                }


                if (_config.AutoStartChatBot && !IsChatBotRunning && _currentMode == AppMode.Full)
                {
                    StartChatBot();
                }



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
            if (_currentMode == AppMode.Full && !IsConnected)
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
            if (_currentMode == AppMode.Full && !IsConnected)
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

        private async Task CheckGameStatusAsync()
        {
            if (!IsMonitoring || (_currentMode == AppMode.Full && !IsConnected)) return;

            try
            {
                var currentPrediction = await _predictionService.GetCurrentPredictionAsync();
                if (currentPrediction != null)
                {
                    CurrentPrediction = currentPrediction;

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

        // ========== Чат-бот ==========

        private void StartChatBot()
        {
            try
            {
                if (string.IsNullOrEmpty(BotUsername) || string.IsNullOrEmpty(BotAccessToken))
                {
                    Log("⚠️ Данные чат-бота не заполнены");
                    return;
                }

                if (!IsConnected)
                {
                    Log("⚠️ Сначала подключитесь к Twitch");
                    return;
                }

                _chatService = new TwitchChatService(
                    BotUsername,
                    BotAccessToken,
                    ChannelName,
                    _sessionStats);

                _chatService.OnLogMessage += (msg) => Log(msg);
                _chatService.Connect();

                IsChatBotRunning = true;
                Log("🤖 Чат-бот запущен");
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка запуска чат-бота: {ex.Message}");
            }
        }

        private void StopChatBot()
        {
            try
            {
                _chatService?.Disconnect();
                _chatService = null;
                IsChatBotRunning = false;
                Log("🛑 Чат-бот остановлен");
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка остановки чат-бота: {ex.Message}");
            }
        }

        // ========== События Dota 2 ==========

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

                if (_currentMode == AppMode.Full && _config.AutoCreatePredictions && IsConnected)
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

        private async Task CreatePredictionForMatch(Dota2Match match)
        {
            try
            {
                if (match == null)
                {
                    Log("❌ CreatePredictionForMatch: match is null");
                    return;
                }

                var title = $"Победит ли стример?";
                var outcomes = new[] { "Win", "Lose" }; // ← ИЗМЕНЕНО

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

        private async void OnGSIGameEnded(object sender, Dota2Match match)
        {
            try
            {
                IsGameRunning = false;

                if (match == null)
                {
                    Log("ℹ️ Получен сигнал об окончании игры (дисконнект)");

                    if (_currentMode == AppMode.Full && _config.AutoEndPredictions && CurrentPrediction != null)
                    {
                        Log("🚫 Дисконнект - отменяем ставку...");
                        await CancelPredictionOnDisconnect();
                    }

                    CurrentMatch = null;
                    await CleanupAfterGame();
                    return;
                }

                if (match.Winner == "CANCELED" || match.Status == MatchStatus.Canceled)
                {
                    Log($"🚫 МАТЧ ОТМЕНЕН из-за дисконнекта!");

                    if (_currentMode == AppMode.Full && _config.AutoEndPredictions && CurrentPrediction != null)
                    {
                        await CancelPredictionOnDisconnect();
                    }

                    CurrentMatch = null;
                    await CleanupAfterGame();
                    return;
                }

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

                if (_currentMode == AppMode.Full && _config.AutoEndPredictions && CurrentPrediction != null)
                {
                    await Task.Delay(5000);
                    await EndPredictionForMatch(match);
                }

                SaveConfigToModel();
                _config.Save();

                await CleanupAfterGame();
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка в OnGSIGameEnded: {ex.Message}");
            }
        }

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
                    _gameService.ResetPredictionFlag();
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

                // Определяем, победил ли стример
                bool playerWon = (match.Winner == match.PlayerTeam);  
                string winningOutcomeTitle = playerWon ? "Win" : "Lose";

                Log($"🏆 Авто-завершение ставки в пользу: {winningOutcomeTitle}");

                string winningOutcomeId = null;
                foreach (var outcome in CurrentPrediction.Outcomes)
                {
                    if (outcome.Title == winningOutcomeTitle)
                    {
                        winningOutcomeId = outcome.Id;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(winningOutcomeId))
                {
                    winningOutcomeId = CurrentPrediction.Outcomes[0].Id;
                    Log($"⚠️ Не найден outcome для {winningOutcomeTitle}, использую первый");
                }

                var success = await _predictionService.EndPredictionAsync(winningOutcomeId);

                if (success)
                {
                    Log($"✅ Ставка авто-завершена в пользу {winningOutcomeTitle}!");
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

        private async Task CleanupAfterGame()
        {
            try
            {
                Log("🧹 Очистка после игры...");
                await Task.Delay(1000);

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

        // ========== Ручные операции ==========

        

        private async Task CreatePredictionAsync()
        {
            if (_currentMode != AppMode.Full)
            {
                Log("⚠️ Создание ставки доступно только в полном режиме");
                return;
            }

            if (!IsConnected)
            {
                Log("⚠️ Сначала подключитесь к Twitch");
                return;
            }

            try
            {
                var title = $"Победит ли стример?";
                var outcomes = new[] { "Win", "Lose" };

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

        private async Task LockPredictionAsync()
        {
            if (_currentMode != AppMode.Full)
            {
                Log("⚠️ Управление ставками доступно только в полном режиме");
                return;
            }

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

        private async Task EndPredictionAsync(string winner)
        {
            if (_currentMode != AppMode.Full)
            {
                Log("⚠️ Управление ставками доступно только в полном режиме");
                return;
            }

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

                // Ищем точное совпадение с Win или Lose
                string winningOutcomeId = "";
                foreach (var outcome in CurrentPrediction.Outcomes)
                {
                    if (outcome.Title.Equals(winner, StringComparison.OrdinalIgnoreCase))
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
                    _gameService.ResetPredictionFlag(); // Сбрасываем флаг для следующей игры
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
        private async Task CancelPredictionAsync()
        {
            if (_currentMode != AppMode.Full)
            {
                Log("⚠️ Управление ставками доступно только в полном режиме");
                return;
            }

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
                    _gameService.ResetPredictionFlag();
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

        // ========== Логи ==========

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

    



    
            public void Log(string message, bool showTimestamp = true)
        {
            try
            {
                var timestamp = showTimestamp ? $"[{DateTime.Now:HH:mm:ss}] " : "";
                var line = timestamp + message;

                // Добавляем в список для UI
                _logLines.Add(line);

                if (_logLines.Count > MAX_LOG_LINES + 100)
                {
                    int removedCount = _logLines.Count - MAX_LOG_LINES;
                    _logLines.RemoveRange(0, removedCount);
                }

                // Обновляем UI
                LogText = string.Join("\n", _logLines);
                OnPropertyChanged(nameof(LogText));

                // ========== ЗАПИСЫВАЕМ В ФАЙЛ ==========
                Utils.FileLogger.WriteLine(line);
                // =====================================
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка логирования: {ex.Message}");
            }
        }
    }
    }
