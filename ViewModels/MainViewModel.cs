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
        Tracker,
        Full
    }

    public class MainViewModel : ViewModelBase
    {
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
        private bool _isChatBotRunning = false;

        // Для комбинированных ставок
        private bool _waitingForFirstBlood = false;
        private PredictionType _pendingPredictionType = PredictionType.WinLose;
       
        // Настройки
        private bool _automationEnabled;
        private int _predictionWindowSeconds;
        private int _pendingWindowSeconds = 0;
        private int _gsiPort;
        private int _currentMmr;
        private PredictionType _selectedPredictionType;
        private bool _autoStartChatBot;

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

        public bool IsChatBotRunning
        {
            get => _isChatBotRunning;
            set => SetProperty(ref _isChatBotRunning, value);
        }

        public bool AutomationEnabled
        {
            get => _automationEnabled;
            set
            {
                if (SetProperty(ref _automationEnabled, value))
                {
                    _config.AutomationEnabled = value;
                    _config.Save();
                    Log($"⚙️ Автоматизация: {(value ? "включена" : "выключена")}");
                }
            }
        }

        public int PredictionWindowSeconds
        {
            get => _predictionWindowSeconds;
            set
            {
                if (SetProperty(ref _predictionWindowSeconds, value))
                {
                    _config.PredictionWindowSeconds = value;
                    _config.Save();
                    Log($"⚙️ Время приёма ставок изменено: {value} секунд");
                }
            }
        }

       

        public int GsiPort
        {
            get => _gsiPort;
            set
            {
                if (SetProperty(ref _gsiPort, value))
                {
                    _config.GSIPort = value;
                    _config.Save();
                    MessageBox.Show(
                        "⚠️ Порт GSI требует перезапуска приложения.\n\nДля применения изменений:\n1. Закройте приложение\n2. Удалите старый GSI конфиг из папки Dota 2\n3. Запустите приложение заново",
                        "Изменение порта GSI",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    Log($"📌 Порт GSI изменён на {value}. Применится после перезапуска.");
                }
            }
        }

        public int CurrentMmr
        {
            get => _currentMmr;
            set
            {
                if (SetProperty(ref _currentMmr, value))
                {
                    _config.CurrentMmr = value;
                    _config.Save();
                    _sessionStats?.SetMmr(value);
                    Log($"📊 MMR изменён вручную: {value}");
                    OnPropertyChanged(nameof(RankTitle));
                }
            }
        }

        public PredictionType SelectedPredictionType
        {
            get => _selectedPredictionType;
            set
            {
                if (SetProperty(ref _selectedPredictionType, value))
                {
                    _config.SelectedPredictionType = value;
                    _config.Save();
                    Log($"🎲 Тип ставки изменён: {GetPredictionTypeName(value)}");
                }
            }
        }

        public bool AutoStartChatBot
        {
            get => _autoStartChatBot;
            set
            {
                if (SetProperty(ref _autoStartChatBot, value))
                {
                    _config.AutoStartChatBot = value;
                    _config.Save();
                    Log($"⚙️ Автозапуск чат-бота: {(value ? "включён" : "выключен")}");
                }
            }
        }

        public string RankTitle => GetRankTitle(CurrentMmr);

        public string AccessToken { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string ChannelName { get; set; } = "";
        public string BroadcasterId { get; set; } = "";
        public string BotUsername { get; set; } = "";
        public string BotAccessToken { get; set; } = "";

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
        public ICommand StartChatBotCommand { get; }
        public ICommand StopChatBotCommand { get; }
        public ICommand DeleteGSIConfigCommand { get; }
        public ICommand ResetGameEventsCommand { get; }
        public ICommand GetTokenViaOAuthCommand { get; }
        public MainViewModel()
        {
            _config = AppConfig.Load();

            _automationEnabled = _config.AutomationEnabled;
            _predictionWindowSeconds = _config.PredictionWindowSeconds;
           
            _gsiPort = _config.GSIPort;
            _currentMmr = _config.CurrentMmr;
            _selectedPredictionType = _config.SelectedPredictionType;
            _autoStartChatBot = _config.AutoStartChatBot;

            AccessToken = _config.AccessToken;
            ClientId = _config.ClientId;
            ChannelName = _config.ChannelName;
            BroadcasterId = _config.BroadcasterId;
            BotUsername = _config.BotUsername;
            BotAccessToken = _config.BotAccessToken;

            _authService = new TwitchAuthService();
            _predictionService = new PredictionService(_config);
            _openDotaService = new OpenDotaService((msg) => Log(msg));
            _gameService = new Dota2GameService(_config, this, _openDotaService);
          
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

            _gameService.OnGameStarted += (match) => OnGSIGameStarted(match);
            _gameService.OnGameEnded += (match) => OnGSIGameEnded(match);
            _gameService.OnFirstBlood += (team, gameTime, match) => OnFirstBloodEvent(team, gameTime, match);
            _gameService.OnRoshanKill += OnRoshanKillEvent;

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
            StartChatBotCommand = new RelayCommand(StartChatBot);
            StopChatBotCommand = new RelayCommand(StopChatBot);
            ResetGameEventsCommand = new RelayCommand(ResetGameEvents);
            GetTokenViaOAuthCommand = new RelayCommand(() => _ = GetTokenViaOAuthAsync());
            _monitoringTimer = new System.Timers.Timer(30000);
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

            StartGSI();
        }

        private void SaveConfig()
        {
            _config.AccessToken = AccessToken;
            _config.ClientId = ClientId;
            _config.ChannelName = ChannelName;
            _config.BroadcasterId = BroadcasterId;
            _config.BotUsername = BotUsername;
            _config.BotAccessToken = BotAccessToken;
            _config.CurrentMmr = _sessionStats.CurrentMmr;
            _config.AutomationEnabled = AutomationEnabled;
            _config.PredictionWindowSeconds = PredictionWindowSeconds;
            
            _config.GSIPort = GsiPort;
            _config.SelectedPredictionType = SelectedPredictionType;
            _config.AutoStartChatBot = AutoStartChatBot;
            _config.Save();
            Log("💾 Конфиг сохранён");
        }

        private string GetPredictionTypeName(PredictionType type)
        {
            switch (type)
            {
                case PredictionType.WinLose: return "Win/Lose";
                case PredictionType.FirstBlood: return "First Blood";
                case PredictionType.RoshanKill: return "RoshanKill";
                case PredictionType.FirstBloodThenWinLose: return "First Blood → Win/Lose";
                case PredictionType.FirstBloodThenRoshanKill: return "First Blood → RoshanKill";
                default: return "Unknown";
            }
        }

        private string GetRankTitle(int mmr)
        {
            if (mmr == 0) return "Не определён";
            if (mmr < 770) return "Herald";
            if (mmr < 1540) return "Guardian";
            if (mmr < 2310) return "Crusader";
            if (mmr < 3080) return "Archon";
            if (mmr < 3850) return "Legend";
            if (mmr < 4620) return "Ancient";
            if (mmr < 5420) return "Divine";
            return "Immortal";
        }

       

        private void ResetGameEvents()
        {
            _waitingForFirstBlood = false;
          
        
            Log("🔄 Сброс состояния комбинированных ставок");
        }

        private void ClearOldPrediction()
        {
            CurrentPrediction = null;
            try
            {
                var field = typeof(PredictionService).GetField("_currentPrediction",
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

                _config.BroadcasterId = await _authService.GetBroadcasterId(AccessToken, ClientId, ChannelName);
                if (string.IsNullOrEmpty(_config.BroadcasterId))
                {
                    Log("❌ Не удалось получить ID канала");
                    return false;
                }

                BroadcasterId = _config.BroadcasterId;
                Log($"📺 Канал: {ChannelName} (ID: {BroadcasterId})");

                SaveConfig();

                var current = await _predictionService.GetCurrentPredictionAsync();
                if (current != null)
                {
                    CurrentPrediction = current;
                    Log($"📊 Найдена активная ставка: {current.Title}");
                }

                IsConnected = true;
                Log("✅ Подключено к Twitch!");

                if (_currentMode == AppMode.Full && !IsMonitoring && AutomationEnabled)
                {
                    StartMonitoring();
                }

                if (_currentMode == AppMode.Full && !IsChatBotRunning && AutoStartChatBot)
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

        private void ToggleMonitoring()
        {
            if (_currentMode == AppMode.Full && !IsConnected)
            {
                Log("⚠️ Сначала подключитесь к Twitch");
                return;
            }

            if (IsMonitoring)
                StopMonitoring();
            else
                StartMonitoring();
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

        private void StartGSI()
        {
            try
            {
                _gameService.Start();
                IsGameRunning = _gameService.IsGameRunning();
                Log("🎮 Dota2 GSI запущен");
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

                _chatService = new TwitchChatService(BotUsername, BotAccessToken, ChannelName, _sessionStats);
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

        private async void OnGSIGameStarted(Dota2Match match)
        {
          

            CurrentMatch = match;
           

            IsGameRunning = true;
            Log($"🎮 ИГРА НАЧАЛАСЬ!");

           
           
           

            try
            {
                if (match == null)
                {
                    Log("❌ OnGSIGameStarted: match is null");
                    return;
                }

                if (_currentMode == AppMode.Full && AutomationEnabled && IsConnected)
                {
                    Log("🔄 Принудительное обновление данных Twitch...");
                    await _predictionService.ForceRefresh();
                    await Task.Delay(2000);

                    CurrentPrediction = null;
                    _waitingForFirstBlood = false;
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка в OnGSIGameStarted: {ex.Message}");
            }
        }

        public async Task CreatePredictionForMatch(Dota2Match match, PredictionType predictionType, int customWindowSeconds = 0)
        {
            
            try
            {
                if (match == null)
                {
                    Log("❌ CreatePredictionForMatch: match is null");
                    return;
                }

                string title;
                string[] outcomes;
                int windowSeconds = customWindowSeconds > 0 ? customWindowSeconds : _config.PredictionWindowSeconds;

                switch (predictionType)
                {
                    case PredictionType.WinLose:
                        title = "Победит ли стример? (Win/Lose)";
                        outcomes = new[] { "Win", "Lose" };
                        await ExecutePredictionCreation(title, outcomes, windowSeconds);
                        break;

                    case PredictionType.FirstBlood:
                        title = "First Blood: Radiant или Dire?";
                        outcomes = new[] { "Radiant", "Dire" };
                        await ExecutePredictionCreation(title, outcomes, 90); // 90 сек на FB
                        break;

                    case PredictionType.RoshanKill:
                        title = "Кто первым убьёт Рошана?";
                        outcomes = new[] { "Radiant", "Dire" };
                        await ExecutePredictionCreation(title, outcomes, windowSeconds);
                        break;

                    case PredictionType.FirstBloodThenWinLose:
                        title = "First Blood: Radiant или Dire?";
                        outcomes = new[] { "Radiant", "Dire" };
                        _waitingForFirstBlood = true;
                        _pendingPredictionType = PredictionType.WinLose;
                        _pendingWindowSeconds = 150;
                        await ExecutePredictionCreation(title, outcomes, 90); // 90 сек на FB
                        break;

                    case PredictionType.FirstBloodThenRoshanKill:
                        title = "First Blood: Radiant или Dire?";
                        outcomes = new[] { "Radiant", "Dire" };
                        _waitingForFirstBlood = true;
                        _pendingPredictionType = PredictionType.RoshanKill;
                        _pendingWindowSeconds = 180; 
                        await ExecutePredictionCreation(title, outcomes, 90); // 90 сек на FB
                        break;

                    default:
                        title = "Победит ли стример? (Win/Lose)";
                        outcomes = new[] { "Win", "Lose" };
                        await ExecutePredictionCreation(title, outcomes, windowSeconds);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка создания авто-ставки: {ex.Message}");
            }
        }

        private async Task ExecutePredictionCreation(string title, string[] outcomes, int windowSeconds)
        {
            var prediction = await _predictionService.Async(title, outcomes, windowSeconds);

            if (prediction != null)
            {
                CurrentPrediction = prediction;
                Log($"✅ Авто-ставка создана!");
                Log($"⏱️ Прием прогнозов: {windowSeconds} секунд");
            }
            else
            {
                Log("❌ Не удалось создать авто-ставку");
            }
        }

        private async void OnFirstBloodEvent(string team, double gameTime, Dota2Match match)
        {
           

            try
            {
                Log($"🔥 FIRST BLOOD! Команда {team} убила первой на {gameTime:F0} секунде");

                if (CurrentPrediction != null && CurrentPrediction.Title.Contains("First Blood"))
                {
                    Log($"🏆 Завершаем ставку First Blood в пользу {team}");
                    await EndPredictionByOutcomeTitle(team);
                }

                if (_waitingForFirstBlood)
                {
                    _waitingForFirstBlood = false;

                 
                    var savedMatch = match;
                    var pendingType = _pendingPredictionType;
                    var windowSec = _pendingWindowSeconds;
                    Log($"🔄 Ждём 10 секунд перед созданием следующей ставки...");
                    await Task.Delay(10000);

                    if (savedMatch != null)
                    {
                       
                        await CreatePredictionForMatch(savedMatch, pendingType, windowSec);
                    }
                    else
                    {
                        Log($"⚠️ savedMatch = null, не могу создать ставку");
                    }
                }
                else
                {
                    Log($"⚠️ _waitingForFirstBlood = False, пропускаем создание второй ставки");
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка обработки First Blood: {ex.Message}");
            }
        }

        private async void OnRoshanKillEvent(string team, double gameTime)
        {
            try
            {
                
                Log($"🔍 OnRoshanKillEvent: Установлен _roshanKilledThisGame = true (Рошан убит командой {team} на {gameTime} сек)");

                Log($"👑 RoshanKill! Команда {team} первой убила Рошана на {gameTime:F0} секунде");

               
                if (CurrentPrediction != null && CurrentPrediction.Title.Contains("убьёт Рошана"))
                {
                    Log($"🏆 Завершаем ставку RoshanKill в пользу {team}");
                    await EndPredictionByOutcomeTitle(team);
                }
                else
                {
                    
                    Log($"⚠️ Не удалось завершить ставку RoshanKill. CurrentPrediction is null? {CurrentPrediction == null}. Title: '{CurrentPrediction?.Title}'");
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка обработки RoshanKill: {ex.Message}");
            }
        }

        private async Task EndPredictionByOutcomeTitle(string outcomeTitle)
        {
            if (CurrentPrediction == null)
            {
                Log("⚠️ Нет активной ставки для завершения");
                return;
            }

            if (!IsConnected)
            {
                Log("⚠️ Нет подключения к Twitch");
                return;
            }

            try
            {
                string winningOutcomeId = "";
                foreach (var outcome in CurrentPrediction.Outcomes)
                {
                    if (outcome.Title.Equals(outcomeTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        winningOutcomeId = outcome.Id;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(winningOutcomeId))
                {
                    Log($"❌ Не найден outcome для: {outcomeTitle}");
                    return;
                }

                var success = await _predictionService.EndPredictionAsync(winningOutcomeId);
                if (success)
                {
                    Log($"✅ Ставка завершена в пользу {outcomeTitle}!");
                    CurrentPrediction = null;
                    _gameService.ResetPredictionFlag();
                }
                else
                {
                    Log("❌ Не удалось завершить ставку");
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка завершения ставки: {ex.Message}");
            }
        }


        private async void OnGSIGameEnded(Dota2Match match)
        {
            try
            {
                IsGameRunning = false;

             
                

              
                await CancelRoshanKillPredictionIfNoKill();

                if (match == null || match.Winner == "CANCELED" || match.Status == MatchStatus.Canceled)
                {
                    Log($"🚫 МАТЧ ОТМЕНЕН!");

                    if (_currentMode == AppMode.Full && AutomationEnabled && CurrentPrediction != null)
                    {
                        await CancelPredictionOnDisconnect();
                    }

                    CurrentMatch = null;
                    Log($"🔍 CurrentMatch установлен в NULL");
                    _waitingForFirstBlood = false;
                    await CleanupAfterGame();
                    return;
                }

                Log($"🏁 ИГРА ЗАВЕРШЕНА!");
                if (match.Duration != null) Log($"⏱️ Длительность: {match.Duration:mm\\:ss}");
                if (!string.IsNullOrEmpty(match.Winner)) Log($"🏆 Победитель: {match.Winner}");

                if (_currentMode == AppMode.Full && AutomationEnabled && CurrentPrediction != null)
                {
                    if (CurrentPrediction.Title.Contains("Win/Lose") || CurrentPrediction.Title.Contains("Победит"))
                    {
                        bool playerWon = (match.Winner == match.PlayerTeam);
                        string winningOutcome = playerWon ? "Win" : "Lose";
                        Log($"🏆 Авто-завершение ставки Win/Lose в пользу: {winningOutcome}");
                        await EndPredictionByOutcomeTitle(winningOutcome);
                    }
                }

                CurrentMatch = null;
                _waitingForFirstBlood = false;

                SaveConfig();
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
                string title;
                string[] outcomes;
                int windowSeconds = 0; 
                switch (SelectedPredictionType)
                {
                    case PredictionType.WinLose:
                        title = "Победит ли стример? (Win/Lose)";
                        outcomes = new[] { "Win", "Lose" };
                        windowSeconds = _config.PredictionWindowSeconds;
                        break;
                    case PredictionType.FirstBlood:
                        title = "First Blood: Radiant или Dire?";
                        outcomes = new[] { "Radiant", "Dire" };
                        windowSeconds = 90; // 90 секунд для FB
                        break;
                    case PredictionType.RoshanKill:
                        title = "Кто первым убьёт Рошана?";
                        outcomes = new[] { "Radiant", "Dire" };
                        windowSeconds = 180; // 180 секунд для RoshanKill
                        break;
                    case PredictionType.FirstBloodThenWinLose:
                        title = "First Blood: Radiant или Dire?";
                        outcomes = new[] { "Radiant", "Dire" };
                        windowSeconds = 90; // 90 секунд для FB
                        _waitingForFirstBlood = true;
                        _pendingPredictionType = PredictionType.WinLose;
                        _pendingWindowSeconds = 150;
                        break;
                    case PredictionType.FirstBloodThenRoshanKill:
                        title = "First Blood: Radiant или Dire?";
                        outcomes = new[] { "Radiant", "Dire" };
                        windowSeconds = 90; // 90 секунд для FB
                        _waitingForFirstBlood = true;
                        _pendingPredictionType = PredictionType.RoshanKill;
                        _pendingWindowSeconds = 180; // 180 секунд для RoshanKill
                        break;
                    default:
                        title = "Победит ли стример? (Win/Lose)";
                        outcomes = new[] { "Win", "Lose" };
                        windowSeconds = _config.PredictionWindowSeconds;
                        break;
                }

                Log($"🎲 Создание ставки: {title} (окно: {windowSeconds} сек)");
                var prediction = await _predictionService.Async(title, outcomes, windowSeconds);

                if (prediction != null)
                {
                    CurrentPrediction = prediction;
                    Log($"✅ Ставка создана!");
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
                if (success) Log("✅ Прием прогнозов закрыт");
                else Log("❌ Не удалось закрыть ставку");
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
                    _gameService.ResetPredictionFlag();
                    //_waitingForFirstBlood = false;
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
                    //_waitingForFirstBlood = false;
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
            _gameService.ResetPredictionFlag();
            //_waitingForFirstBlood = false;
        }

        private void CleanupOldLogs()
        {
            try
            {
                if (_logLines.Count > MAX_LOG_LINES)
                {
                    int removedCount = _logLines.Count - MAX_LOG_LINES;
                    _logLines.RemoveRange(0, removedCount);
                    LogText = string.Join("\n", _logLines);
                    OnPropertyChanged(nameof(LogText));
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

                _logLines.Add(line);
                if (_logLines.Count > MAX_LOG_LINES + 100)
                {
                    _logLines.RemoveRange(0, _logLines.Count - MAX_LOG_LINES);
                }

                LogText = string.Join("\n", _logLines);
                OnPropertyChanged(nameof(LogText));
                Utils.FileLogger.WriteLine(line);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка логирования: {ex.Message}");
            }
        }

        private async Task CancelRoshanKillPredictionIfNoKill()
        {
            try
            {
               

                if (CurrentPrediction != null)
                {
                    

                    if (CurrentPrediction.Title.Contains("убьёт Рошана"))
                    {
                        
                        bool wasRoshanKilled = _gameService.WasRoshanKilled;
                        

                        if (!wasRoshanKilled)
                        {
                            Log($"⚠️ Рошан не был убит за игру. Отменяем ставку RoshanKill.");
                            bool success = await _predictionService.CancelPredictionAsync();
                            if (success)
                            {
                                CurrentPrediction = null;
                                _gameService.ResetPredictionFlag();
                                Log($"✅ Ставка RoshanKill отменена (Рошан не убит)");
                            }
                            else
                            {
                                Log($"❌ Не удалось отменить ставку RoshanKill через API");
                            }
                        }
                        else
                        {
                            Log($"✅ CancelRoshanKillPredictionIfNoKill: Рошан был убит, ставка будет завершена обычным путём");
                        }
                    }
                    else
                    {
                        Log($"🔍 CancelRoshanKillPredictionIfNoKill: Ставка не RoshanKill, пропускаем отмену");
                    }
                }
                else
                {
                    Log($"🔍 CancelRoshanKillPredictionIfNoKill: CurrentPrediction = null, пропускаем отмену");
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка отмены RoshanKill ставки: {ex.Message}");
            }
        }
        private async Task GetTokenViaOAuthAsync()
        {
            Log("🔐 Открываю окно авторизации Twitch...");
            var tokenService = new TwitchTokenService(_config);
            string token = await tokenService.GetAccessTokenAsync();

            if (!string.IsNullOrEmpty(token))
            {
                Log($"✅ Токен успешно получен и сохранён!");
                AccessToken = token;

                if (IsConnected)
                {
                    Log("🔄 Переподключаемся к Twitch с новым токеном...");
                    await ConnectToTwitchAsync();
                }
            }
            else
            {
                Log("❌ Не удалось получить токен. Авторизация отменена или произошла ошибка.");
            }
        }


    }
}