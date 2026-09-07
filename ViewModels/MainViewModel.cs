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
        // DonationAlerts + музыка
        private readonly DonationAlertsAuthService _daAuthService;
        private readonly DonationAlertsService _daService;
        private readonly YtDlpService _ytDlpService;
        private readonly CurrencyService _currencyService;
        private readonly MusicQueueService _musicService;
        private readonly ChromeController _chrome;
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
        private bool _isDonationAlertsConnected;
        private string _donationAlertsStatus = "Не подключено";
        private string _currentTrackText = "Сейчас ничего не играет";
        private string _manualTrackQuery = "";
        private double _minDonationAmountRub;
        private string _musicCommand;
        private string _ytDlpPath;
        private bool _musicAutoAdvance;
        private int _musicMaxDurationSeconds;
        private bool _donationAlertsEnabled;
        private bool _autoStartDonationAlerts;
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
                    Log($"Автоматизация: {(value ? "включена" : "выключена")}");
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
                    Log($"Время приёма ставок изменено: {value} секунд");
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
                        "Порт GSI требует перезапуска приложения.\n\nДля применения изменений:\n1. Закройте приложение\n2. Удалите старый GSI конфиг из папки Dota 2\n3. Запустите приложение заново",
                        "Изменение порта GSI",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    Log($"Порт GSI изменён на {value}. Применится после перезапуска.");
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
                    Log($"MMR изменён вручную: {value}");
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
                    Log($"Тип ставки изменён: {GetPredictionTypeName(value)}");
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
                    Log($"Автозапуск чат-бота: {(value ? "включён" : "выключен")}");
                }
            }
        }
        // ================= DonationAlerts / Музыка =================
        public bool IsDonationAlertsConnected
        {
            get => _isDonationAlertsConnected;
            set => SetProperty(ref _isDonationAlertsConnected, value);
        }
        public string DonationAlertsStatus
        {
            get => _donationAlertsStatus;
            set => SetProperty(ref _donationAlertsStatus, value);
        }
        public string CurrentTrackText
        {
            get => _currentTrackText;
            set => SetProperty(ref _currentTrackText, value);
        }
        public string ManualTrackQuery
        {
            get => _manualTrackQuery;
            set => SetProperty(ref _manualTrackQuery, value);
        }
        public System.Collections.ObjectModel.ObservableCollection<MusicRequest> MusicQueue => _musicService.Queue;
        public string DonationAlertsClientId { get; set; } = "";
        public string DonationAlertsClientSecret { get; set; } = "";
        public string DonationAlertsRedirectUri { get; set; } = "";
        public string DonationAlertsAccessToken { get; set; } = "";
        public string DonationAlertsRefreshToken { get; set; } = "";
        public string DonationAlertsUserId
        {
            get => _config.DonationAlertsBroadcasterId;
            set
            {
                _config.DonationAlertsBroadcasterId = value;
                OnPropertyChanged(nameof(DonationAlertsUserId));
            }
        }
        public double MinDonationAmountRub
        {
            get => _minDonationAmountRub;
            set
            {
                if (SetProperty(ref _minDonationAmountRub, value))
                {
                    _config.MinDonationAmountRub = value;
                    _config.Save();
                    if (_chatService != null) _chatService.MinAmountRub = value;
                    Log($"Минимальный донат для заказа трека: {value} ₽");
                }
            }
        }
        public string MusicCommand
        {
            get => _musicCommand;
            set
            {
                if (SetProperty(ref _musicCommand, value))
                {
                    _config.MusicCommand = value;
                    _config.Save();
                }
            }
        }
        public string YtDlpPath
        {
            get => _ytDlpPath;
            set
            {
                if (SetProperty(ref _ytDlpPath, value))
                {
                    _config.YtDlpPath = value;
                    _config.Save();
                }
            }
        }
        public bool MusicAutoAdvance
        {
            get => _musicAutoAdvance;
            set
            {
                if (SetProperty(ref _musicAutoAdvance, value))
                {
                    _config.MusicAutoAdvance = value;
                    _config.Save();
                }
            }
        }
        public int MusicMaxDurationSeconds
        {
            get => _musicMaxDurationSeconds;
            set
            {
                if (SetProperty(ref _musicMaxDurationSeconds, value))
                {
                    _config.MusicMaxDurationSeconds = value;
                    _config.Save();
                }
            }
        }
        // ================= Chrome / пауза музыки стримера =================
        private bool _pauseStreamerMusic;
        private int _chromeDebugPort;
        /// <summary>Паузить музыку стримера (YouTube-вкладка Chrome) на время донатного трека</summary>
        public bool PauseStreamerMusic
        {
            get => _pauseStreamerMusic;
            set
            {
                if (SetProperty(ref _pauseStreamerMusic, value))
                {
                    _config.PauseStreamerMusic = value;
                    _config.Save();
                    Log($"Настройка: пауза музыки стримера {(value ? "включена" : "выключена")}");
                }
            }
        }
        /// <summary>Порт удалённой отладки Chrome (нужен перезапуск Chrome с флагом)</summary>
        public int ChromeDebugPort
        {
            get => _chromeDebugPort;
            set
            {
                if (SetProperty(ref _chromeDebugPort, value))
                {
                    _config.ChromeDebugPort = value;
                    _config.Save();
                    Log($"Порт отладки Chrome изменён на {value}. Chrome нужно перезапустить с флагом --remote-debugging-port={value}");
                }
            }
        }

        private bool _blockAds;

        /// <summary>Автоматически пропускать рекламу YouTube во вкладках с музыкой</summary>
        public bool BlockAds
        {
            get => _blockAds;
            set
            {
                if (SetProperty(ref _blockAds, value))
                {
                    _config.BlockAds = value;
                    _config.Save();
                    Log($"Настройка: пропуск рекламы YouTube {(value ? "включён" : "выключен")}");
                }
            }
        }
        public bool DonationAlertsEnabled
        {
            get => _donationAlertsEnabled;
            set
            {
                if (SetProperty(ref _donationAlertsEnabled, value))
                {
                    _config.DonationAlertsEnabled = value;
                    _config.Save();
                    if (!value && _daService.IsRunning)
                        _daService.Stop();
                }
            }
        }
        public bool AutoStartDonationAlerts
        {
            get => _autoStartDonationAlerts;
            set
            {
                if (SetProperty(ref _autoStartDonationAlerts, value))
                {
                    _config.AutoStartDonationAlerts = value;
                    _config.Save();
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
        public ICommand ResetGameEventsCommand { get; }
        public ICommand GetTokenViaOAuthCommand { get; }
        public ICommand AuthorizeDonationAlertsCommand { get; }
        public ICommand AuthorizeDonationAlertsViaOAuthCommand { get; }
        public ICommand StartDonationAlertsCommand { get; }
        public ICommand StopDonationAlertsCommand { get; }
        public ICommand LogoutDonationAlertsCommand { get; }
        public ICommand SkipTrackCommand { get; }
        public ICommand ClearQueueCommand { get; }
        public ICommand StopMusicCommand { get; }
        public ICommand AddTrackManuallyCommand { get; }
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
            DonationAlertsClientId = _config.DonationAlertsClientId;
            DonationAlertsClientSecret = _config.DonationAlertsClientSecret;
            DonationAlertsRedirectUri = _config.DonationAlertsRedirectUri;
            DonationAlertsAccessToken = _config.DonationAlertsAccessToken;
            DonationAlertsRefreshToken = _config.DonationAlertsRefreshToken;
            _minDonationAmountRub = _config.MinDonationAmountRub;
            _musicCommand = _config.MusicCommand;
            _ytDlpPath = _config.YtDlpPath;
            _musicAutoAdvance = _config.MusicAutoAdvance;
            _musicMaxDurationSeconds = _config.MusicMaxDurationSeconds;
            _pauseStreamerMusic = _config.PauseStreamerMusic;
            _chromeDebugPort = _config.ChromeDebugPort;
            _blockAds = _config.BlockAds;
            _donationAlertsEnabled = _config.DonationAlertsEnabled;
            _autoStartDonationAlerts = _config.AutoStartDonationAlerts;
            _authService = new TwitchAuthService();
            _predictionService = new PredictionService(_config);
            _openDotaService = new OpenDotaService((msg) => Log(msg));
            _gameService = new Dota2GameService(_config, this, _openDotaService);
            _sessionStats = new SessionStats();
            _gameService.SessionStats = _sessionStats;
            if (_config.CurrentMmr > 0)
            {
                _sessionStats.SetMmr(_config.CurrentMmr);
                Log($"MMR загружен из конфига: {_config.CurrentMmr}");
            }
            _predictionService.OnPredictionCreated += OnPredictionCreated;
            _predictionService.OnPredictionUpdated += OnPredictionUpdated;
            _predictionService.OnPredictionEnded += OnPredictionEnded;
            _gameService.OnGameStarted += (match) => OnGSIGameStarted(match);
            _gameService.OnGameEnded += (match) => OnGSIGameEnded(match);
            _gameService.OnFirstBlood += (team, gameTime, match) => OnFirstBloodEvent(team, gameTime, match);
            _gameService.OnRoshanKill += OnRoshanKillEvent;
            // ===== DonationAlerts + музыка через yt-dlp =====
            _currencyService = new CurrencyService();
            _currencyService.OnLogMessage += (msg) => Log(msg);
            _ytDlpService = new YtDlpService(_config);
            _ytDlpService.OnLogMessage += (msg) => Log(msg);
            _chrome = new ChromeController(_config.ChromeDebugPort);
            _musicService = new MusicQueueService(_config, _ytDlpService, _currencyService, _chrome);
            _musicService.OnLogMessage += (msg) => Log(msg);
            _musicService.OnTrackStarted += (track) =>
            {
                CurrentTrackText = _musicService.GetCurrentMusicInfo();
                if (track == null)
                    _ = UpdateStreamerTrackTextAsync();
            };
            _musicService.OnQueueChanged += () =>
            {
                OnPropertyChanged(nameof(MusicQueue));
            };
            _daAuthService = new DonationAlertsAuthService(_config);
            _daAuthService.OnLogMessage += (msg) => Log(msg);
            _daService = new DonationAlertsService(_config, _daAuthService);
            _daService.OnLogMessage += (msg) => Log(msg);
            _daService.OnConnectionChanged += (connected) =>
            {
                IsDonationAlertsConnected = connected;
                DonationAlertsStatus = connected
                    ? $"Подключено{(string.IsNullOrEmpty(_daService.ConnectedAs) ? "" : $" ({_daService.ConnectedAs})")}"
                    : "Не подключено";
            };
            _daService.OnDonation += (donation) => _ = HandleDonationAsync(donation);
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
            AuthorizeDonationAlertsCommand = new RelayCommand(() => _ = AuthorizeDonationAlertsAsync());
            AuthorizeDonationAlertsViaOAuthCommand = new RelayCommand(() => _ = AuthorizeDonationAlertsViaOAuthAsync());
            StartDonationAlertsCommand = new RelayCommand(StartDonationAlerts);
            StopDonationAlertsCommand = new RelayCommand(() => _daService.Stop());
            LogoutDonationAlertsCommand = new RelayCommand(LogoutDonationAlerts);
            SkipTrackCommand = new RelayCommand(() => _musicService.Skip());
            ClearQueueCommand = new RelayCommand(() => _musicService.ClearQueue());
            StopMusicCommand = new RelayCommand(() => _musicService.StopPlayback());
            AddTrackManuallyCommand = new RelayCommand(() => _ = AddTrackManuallyAsync());
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
            Log($"Конфиг: {System.IO.Path.GetFullPath(_config.ConfigPath)}");
            Log("Twitch Bet Bot для Dota 2 запущен");
            Log("Режим: Трекер (только MMR)");
            if (!Utils.SecureStorage.SelfTest())
                Log("DPAPI недоступен — токены не будут расшифрованы корректно");
            if (!_ytDlpService.IsAvailable())
                Log($"yt-dlp не найден ({_ytDlpService.ResolveExecutablePath()}). Заказ музыки работать не будет.");

            _ = CheckChromeAtStartupAsync();

            StartGSI();
            if (_config.DonationAlertsEnabled && _config.AutoStartDonationAlerts && _daAuthService.HasTokens)
                StartDonationAlerts();
        }
        /// <summary>Когда донатная очередь пуста, показывает в статусе трек, который играет у стримера в Chrome.</summary>
        private async Task UpdateStreamerTrackTextAsync()
        {
            try
            {
                // Даём Chrome время возобновить воспроизведение после закрытия вкладки
                await Task.Delay(3000);
                var info = await _musicService.GetCurrentTrackInfoAsync();
                CurrentTrackText = info;
            }
            catch { }
        }

        private async Task CheckChromeAtStartupAsync()
        {
            if (await _chrome.IsAvailableAsync())
            {
                Log($"Chrome найден (порт отладки {_config.ChromeDebugPort}) — пауза музыки стримера и закрытие вкладок доступны");
            }
            else
            {
                Log($"[i] Chrome с режимом отладки не найден. Для паузы музыки стримера и автозакрытия вкладок запустите Chrome с флагами:");
                Log($"    chrome.exe --remote-debugging-port={_config.ChromeDebugPort} --user-data-dir=C:\\ChromeDebug");
                Log("[i] Донатные треки при этом будут открываться в браузере по умолчанию без автозакрытия.");
            }
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
            _config.DonationAlertsClientId = (DonationAlertsClientId ?? "").Trim();
            _config.DonationAlertsClientSecret = (DonationAlertsClientSecret ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(DonationAlertsRedirectUri))
                _config.DonationAlertsRedirectUri = DonationAlertsRedirectUri.Trim();
            _config.MinDonationAmountRub = MinDonationAmountRub;
            _config.MusicCommand = MusicCommand;
            _config.YtDlpPath = YtDlpPath;
            _config.MusicAutoAdvance = MusicAutoAdvance;
            _config.MusicMaxDurationSeconds = MusicMaxDurationSeconds;
            _config.PauseStreamerMusic = PauseStreamerMusic;
            _config.ChromeDebugPort = ChromeDebugPort;
            _config.BlockAds = BlockAds;
            _config.DonationAlertsEnabled = DonationAlertsEnabled;
            _config.AutoStartDonationAlerts = AutoStartDonationAlerts;
            _config.Save();
            Log("Конфиг сохранён (секреты зашифрованы через Windows DPAPI)");
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
            //_gameService.ResetGameEvents();
            Log("Сброс состояния комбинированных ставок");
        }
        private void ClearOldPrediction()
        {
            _predictionService.ClearCurrentPrediction();
            CurrentPrediction = null;
            Log("Очищены данные о старой ставке");
        }
        // Сохраняет текущий MMR из статистики сессии в конфиг (вызывается после каждой игры)
        public void SaveMmrToConfig()
        {
            try
            {
                if (_sessionStats == null) return;
                _currentMmr = _sessionStats.CurrentMmr;
                _config.CurrentMmr = _currentMmr;
                _config.Save();
                OnPropertyChanged(nameof(CurrentMmr));
                OnPropertyChanged(nameof(RankTitle));
                Log($"MMR сохранён в конфиг: {_currentMmr}");
            }
            catch (Exception ex)
            {
                Log($"[!] Ошибка сохранения MMR: {ex.Message}");
            }
        }

        public void SwitchToFullMode()
        {
            if (_currentMode == AppMode.Full) return;
            _currentMode = AppMode.Full;
            Log("Переключено в полный режим (с Twitch)");
            if (!IsConnected && !string.IsNullOrEmpty(AccessToken) && !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ChannelName))
            {
                _ = ConnectToTwitchAsync();
            }
        }
        public void SwitchToTrackerMode()
        {
            if (_currentMode == AppMode.Tracker) return;
            _currentMode = AppMode.Tracker;
            Log("Переключено в режим трекера (только MMR)");
            if (IsChatBotRunning)
            {
                StopChatBot();
            }
        }
        private async Task<bool> ConnectToTwitchAsync()
        {
            try
            {
                Log("Проверка подключения к Twitch...");
                if (string.IsNullOrEmpty(AccessToken))
                {
                    Log("Access Token не заполнен");
                    return false;
                }
                ClearOldPrediction();
                if (string.IsNullOrEmpty(ClientId))
                {
                    Log("Client ID не заполнен");
                    return false;
                }
                if (string.IsNullOrEmpty(ChannelName))
                {
                    Log("Имя канала не заполнено");
                    return false;
                }
                var validation = await _authService.ValidateToken(AccessToken);
                if (validation == null)
                {
                    Log("Неверный Access Token");
                    return false;
                }
                Log($"Токен валиден: {validation.Login}");
                _config.BroadcasterId = await _authService.GetBroadcasterId(AccessToken, ClientId, ChannelName);
                if (string.IsNullOrEmpty(_config.BroadcasterId))
                {
                    Log("Не удалось получить ID канала");
                    return false;
                }
                BroadcasterId = _config.BroadcasterId;
                Log($"Канал: {ChannelName} (ID: {BroadcasterId})");
                SaveConfig();
                var current = await _predictionService.GetCurrentPredictionAsync();
                if (current != null)
                {
                    CurrentPrediction = current;
                    Log($"Найдена активная ставка: {current.Title}");
                }
                IsConnected = true;
                Log("Подключено к Twitch!");
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
                Log($"Ошибка подключения: {ex.Message}");
                return false;
            }
        }
        private void ToggleMonitoring()
        {
            if (_currentMode == AppMode.Full && !IsConnected)
            {
                Log("Сначала подключитесь к Twitch");
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
                Log("Сначала подключитесь к Twitch");
                return;
            }
            _monitoringTimer.Start();
            IsMonitoring = true;
            Log("Мониторинг запущен");
            StartGSI();
        }
        private void StopMonitoring()
        {
            _monitoringTimer.Stop();
            IsMonitoring = false;
            Log("Мониторинг остановлен");
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
                Log($"Ошибка проверки статуса: {ex.Message}");
            }
        }
        private void StartGSI()
        {
            try
            {
                _gameService.Start();
                IsGameRunning = _gameService.IsGameRunning();
                Log("Dota2 GSI запущен");
            }
            catch (Exception ex)
            {
                Log($"Ошибка запуска GSI: {ex.Message}");
            }
        }
        private void StopGSI()
        {
            _gameService.Stop();
            IsGameRunning = false;
            Log("GSI остановлен");
        }
        private void StartChatBot()
        {
            try
            {
                if (string.IsNullOrEmpty(BotUsername) || string.IsNullOrEmpty(BotAccessToken))
                {
                    Log("Данные чат-бота не заполнены");
                    return;
                }
                if (!IsConnected)
                {
                    Log("Сначала подключитесь к Twitch");
                    return;
                }
                _chatService = new TwitchChatService(BotUsername, BotAccessToken, ChannelName, _sessionStats, _musicService);
                _chatService.MinAmountRub = MinDonationAmountRub;
                _chatService.OnLogMessage += (msg) => Log(msg);
                _chatService.Connect();
                IsChatBotRunning = true;
                Log("Чат-бот запущен");
            }
            catch (Exception ex)
            {
                Log($"Ошибка запуска чат-бота: {ex.Message}");
            }
        }
        private void StopChatBot()
        {
            try
            {
                _chatService?.Disconnect();
                _chatService = null;
                IsChatBotRunning = false;
                Log("Чат-бот остановлен");
            }
            catch (Exception ex)
            {
                Log($"Ошибка остановки чат-бота: {ex.Message}");
            }
        }
        private async void OnGSIGameStarted(Dota2Match match)
        {
            CurrentMatch = match;
            IsGameRunning = true;
            Log($"ИГРА НАЧАЛАСЬ!");
            try
            {
                if (match == null)
                {
                    Log("OnGSIGameStarted: match is null");
                    return;
                }
                if (_currentMode == AppMode.Full && AutomationEnabled && IsConnected)
                {
                    Log("Принудительное обновление данных Twitch...");
                    await _predictionService.ForceRefresh();
                    await Task.Delay(2000);
                    CurrentPrediction = null;
                    _waitingForFirstBlood = false;
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка в OnGSIGameStarted: {ex.Message}");
            }
        }
        // Описание ставки: заголовок, варианты и время приёма прогнозов
        private sealed class PredictionBlueprint
        {
            public string Title { get; init; } = "";
            public string[] Outcomes { get; init; } = Array.Empty<string>();
            public int WindowSeconds { get; init; }
            public PredictionType? PendingType { get; init; }   // вторая ставка комбинированного типа
            public int PendingWindowSeconds { get; init; }
        }
        private PredictionBlueprint GetBlueprint(PredictionType type)
        {
            return type switch
            {
                PredictionType.WinLose => new PredictionBlueprint
                {
                    Title = "Победит ли стример? (Win/Lose)",
                    Outcomes = new[] { "Win", "Lose" },
                    WindowSeconds = _config.PredictionWindowSeconds
                },
                PredictionType.FirstBlood => new PredictionBlueprint
                {
                    Title = "First Blood: Radiant или Dire?",
                    Outcomes = new[] { "Radiant", "Dire" },
                    WindowSeconds = 90
                },
                PredictionType.RoshanKill => new PredictionBlueprint
                {
                    Title = "Кто первым убьёт Рошана?",
                    Outcomes = new[] { "Radiant", "Dire" },
                    WindowSeconds = 180
                },
                PredictionType.FirstBloodThenWinLose => new PredictionBlueprint
                {
                    Title = "First Blood: Radiant или Dire?",
                    Outcomes = new[] { "Radiant", "Dire" },
                    WindowSeconds = 90,
                    PendingType = PredictionType.WinLose,
                    PendingWindowSeconds = 150
                },
                PredictionType.FirstBloodThenRoshanKill => new PredictionBlueprint
                {
                    Title = "First Blood: Radiant или Dire?",
                    Outcomes = new[] { "Radiant", "Dire" },
                    WindowSeconds = 90,
                    PendingType = PredictionType.RoshanKill,
                    PendingWindowSeconds = 180
                },
                _ => new PredictionBlueprint
                {
                    Title = "Победит ли стример? (Win/Lose)",
                    Outcomes = new[] { "Win", "Lose" },
                    WindowSeconds = _config.PredictionWindowSeconds
                }
            };
        }
        public async Task CreatePredictionForMatch(Dota2Match match, PredictionType predictionType, int customWindowSeconds = 0)
        {
            try
            {
                if (match == null)
                {
                    Log("CreatePredictionForMatch: match is null");
                    return;
                }
                var bp = GetBlueprint(predictionType);
                int windowSeconds = customWindowSeconds > 0 ? customWindowSeconds : bp.WindowSeconds;
                if (bp.PendingType.HasValue)
                {
                    _waitingForFirstBlood = true;
                    _pendingPredictionType = bp.PendingType.Value;
                    _pendingWindowSeconds = bp.PendingWindowSeconds;
                }
                await ExecutePredictionCreation(bp.Title, bp.Outcomes, windowSeconds);
            }
            catch (Exception ex)
            {
                Log($"Ошибка создания авто-ставки: {ex.Message}");
            }
        }
        private async Task ExecutePredictionCreation(string title, string[] outcomes, int windowSeconds)
        {
            var prediction = await _predictionService.CreatePredictionAsync(title, outcomes, windowSeconds);
            if (prediction != null)
            {
                CurrentPrediction = prediction;
                Log($"Авто-ставка создана!");
                Log($"Прием прогнозов: {windowSeconds} секунд");
            }
            else
            {
                Log("Не удалось создать авто-ставку");
            }
        }
        private async void OnFirstBloodEvent(string team, double gameTime, Dota2Match match)
        {
            try
            {
                Log($"FIRST BLOOD! Команда {team} убила первой на {gameTime:F0} секунде");
                if (CurrentPrediction != null && CurrentPrediction.Title.Contains("First Blood"))
                {
                    Log($"Завершаем ставку First Blood в пользу {team}");
                    await EndPredictionByOutcomeTitle(team);
                }
                if (_waitingForFirstBlood)
                {
                    _waitingForFirstBlood = false;
                    Log($"Ждём 10 секунд перед созданием следующей ставки...");
                    await Task.Delay(10000);
                    if (match != null)
                    {
                        Log($"Вызов CreatePredictionForMatch: match={match.MatchId}, type={_pendingPredictionType}, seconds={_pendingWindowSeconds}");
                        await CreatePredictionForMatch(match, _pendingPredictionType, _pendingWindowSeconds);
                    }
                    else
                    {
                        Log($"match = null, не могу создать ставку");
                    }
                }
                else
                {
                    Log($"_waitingForFirstBlood = False, пропускаем создание второй ставки");
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка обработки First Blood: {ex.Message}");
            }
        }
        private async void OnRoshanKillEvent(string team, double gameTime)
        {
            try
            {
                Log($"RoshanKill! Команда {team} первой убила Рошана на {gameTime:F0} секунде");
                if (CurrentPrediction != null && CurrentPrediction.Title.Contains("убьёт Рошана"))
                {
                    Log($"Завершаем ставку RoshanKill в пользу {team}");
                    await EndPredictionByOutcomeTitle(team);
                }
                else
                {
                    Log($"Нет активной ставки RoshanKill для завершения (текущая: '{CurrentPrediction?.Title ?? "нет"}')");
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка обработки RoshanKill: {ex.Message}");
            }
        }
        private string FindOutcomeId(Prediction prediction, string outcomeTitle)
        {
            foreach (var outcome in prediction.Outcomes)
            {
                if (outcome.Title.Equals(outcomeTitle, StringComparison.OrdinalIgnoreCase))
                    return outcome.Id;
            }
            return "";
        }
        private async Task<bool> EndPredictionByOutcomeTitle(string outcomeTitle)
        {
            if (CurrentPrediction == null)
            {
                Log("Нет активной ставки для завершения");
                return false;
            }
            if (!IsConnected)
            {
                Log("Нет подключения к Twitch");
                return false;
            }
            try
            {
                string winningOutcomeId = FindOutcomeId(CurrentPrediction, outcomeTitle);
                if (string.IsNullOrEmpty(winningOutcomeId))
                {
                    Log($"Не найден outcome для: {outcomeTitle}");
                    return false;
                }
                var success = await _predictionService.EndPredictionAsync(winningOutcomeId);
                if (success)
                {
                    Log($"Ставка завершена в пользу {outcomeTitle}!");
                    CurrentPrediction = null;
                    _gameService.ResetPredictionFlag();
                }
                else
                {
                    Log("Не удалось завершить ставку");
                }
                return success;
            }
            catch (Exception ex)
            {
                Log($"Ошибка завершения ставки: {ex.Message}");
                return false;
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
                    Log($"МАТЧ ОТМЕНЕН!");
                    if (_currentMode == AppMode.Full && AutomationEnabled && CurrentPrediction != null)
                    {
                        await CancelPredictionOnDisconnect();
                    }
                    CurrentMatch = null;
                    _waitingForFirstBlood = false;
                    await CleanupAfterGame();
                    return;
                }
                Log($"ИГРА ЗАВЕРШЕНА!");
                if (match.Duration != null) Log($"Длительность: {match.Duration:mm\\:ss}");
                if (!string.IsNullOrEmpty(match.Winner)) Log($"Победитель: {match.Winner}");
                if (_currentMode == AppMode.Full && AutomationEnabled && CurrentPrediction != null)
                {
                    if (CurrentPrediction.Title.Contains("Win/Lose") || CurrentPrediction.Title.Contains("Победит"))
                    {
                        bool playerWon = (match.Winner == match.PlayerTeam);
                        string winningOutcome = playerWon ? "Win" : "Lose";
                        Log($"Авто-завершение ставки Win/Lose в пользу: {winningOutcome}");
                        await EndPredictionByOutcomeTitle(winningOutcome);
                    }
                }
                CurrentMatch = null;
                _waitingForFirstBlood = false;
                await CleanupAfterGame();
            }
            catch (Exception ex)
            {
                Log($"Ошибка в OnGSIGameEnded: {ex.Message}");
            }
        }
        private async Task CancelPredictionOnDisconnect()
        {
            try
            {
                if (CurrentPrediction == null)
                {
                    Log("Нет активной ставки для отмены");
                    return;
                }
                if (!IsConnected)
                {
                    Log("Нет подключения к Twitch, пробую восстановить...");
                    var connected = await ConnectToTwitchAsync();
                    if (!connected)
                    {
                        Log("Не удалось подключиться к Twitch, ставка не отменена");
                        return;
                    }
                }
                Log($"Отмена ставки: {CurrentPrediction.Title}");
                var success = await _predictionService.CancelPredictionAsync();
                if (success)
                {
                    Log($"Ставка успешно отменена! Баллы возвращены зрителям.");
                    CurrentPrediction = null;
                    _gameService.ResetPredictionFlag();
                }
                else
                {
                    Log("Не удалось отменить ставку через API");
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка отмены ставки: {ex.Message}");
            }
        }
        private async Task CleanupAfterGame()
        {
            Log("Очистка после игры...");
            await Task.Delay(1000);
        }
        private async Task CreatePredictionAsync()
        {
            if (_currentMode != AppMode.Full)
            {
                Log("Создание ставки доступно только в полном режиме");
                return;
            }
            if (!IsConnected)
            {
                Log("Сначала подключитесь к Twitch");
                return;
            }
            try
            {
                var bp = GetBlueprint(SelectedPredictionType);
                if (bp.PendingType.HasValue)
                {
                    _waitingForFirstBlood = true;
                    _pendingPredictionType = bp.PendingType.Value;
                    _pendingWindowSeconds = bp.PendingWindowSeconds;
                }
                Log($"Создание ставки: {bp.Title} (окно: {bp.WindowSeconds} сек)");
                var prediction = await _predictionService.CreatePredictionAsync(bp.Title, bp.Outcomes, bp.WindowSeconds);
                if (prediction != null)
                {
                    CurrentPrediction = prediction;
                    Log($"Ставка создана!");
                }
                else
                {
                    Log("Не удалось создать ставку");
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка: {ex.Message}");
            }
        }
        private async Task LockPredictionAsync()
        {
            if (_currentMode != AppMode.Full)
            {
                Log("Управление ставками доступно только в полном режиме");
                return;
            }
            if (CurrentPrediction == null)
            {
                Log("Нет активной ставки");
                return;
            }
            if (!IsConnected)
            {
                Log("Нет подключения к Twitch");
                return;
            }
            try
            {
                Log("Закрытие приема прогнозов...");
                var success = await _predictionService.LockPredictionAsync();
                if (success) Log("Прием прогнозов закрыт");
                else Log("Не удалось закрыть ставку");
            }
            catch (Exception ex)
            {
                Log($"Ошибка: {ex.Message}");
            }
        }
        private async Task EndPredictionAsync(string winner)
        {
            if (_currentMode != AppMode.Full)
            {
                Log("Управление ставками доступно только в полном режиме");
                return;
            }
            if (CurrentPrediction == null)
            {
                Log("Нет активной ставки");
                return;
            }
            if (!IsConnected)
            {
                Log("Нет подключения к Twitch");
                return;
            }
            try
            {
                Log($"Завершение в пользу: {winner}");
                await EndPredictionByOutcomeTitle(winner);
            }
            catch (Exception ex)
            {
                Log($"Ошибка: {ex.Message}");
            }
        }
        private async Task CancelPredictionAsync()
        {
            if (_currentMode != AppMode.Full)
            {
                Log("Управление ставками доступно только в полном режиме");
                return;
            }
            if (CurrentPrediction == null)
            {
                Log("Нет активной ставки");
                return;
            }
            if (!IsConnected)
            {
                Log("Нет подключения к Twitch");
                return;
            }
            try
            {
                Log("Отмена ставки...");
                var success = await _predictionService.CancelPredictionAsync();
                if (success)
                {
                    Log("Ставка отменена");
                    CurrentPrediction = null;
                    _gameService.ResetPredictionFlag();
                    //_waitingForFirstBlood = false;
                }
                else
                {
                    Log("Не удалось отменить ставку");
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка: {ex.Message}");
            }
        }
        private void OnPredictionCreated(object sender, Prediction prediction)
        {
            Log($"Создана ставка: {prediction.Title}");
        }
        private void OnPredictionUpdated(object sender, Prediction prediction)
        {
            Log($"Статус обновлен: {prediction.Status}");
        }
        private void OnPredictionEnded(object sender, Prediction prediction)
        {
            Log($"Ставка завершена: {prediction.Status}");
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
        // Отменяет ставку на Рошана, если за игру он так и не был убит
        private async Task CancelRoshanKillPredictionIfNoKill()
        {
            try
            {
                if (CurrentPrediction == null || !CurrentPrediction.Title.Contains("убьёт Рошана"))
                    return;
                if (_gameService.WasRoshanKilled)
                    return; // Рошан был убит — ставка завершится обычным путём
                Log($"Рошан не был убит за игру. Отменяем ставку RoshanKill.");
                var success = await _predictionService.CancelPredictionAsync();
                if (success)
                {
                    CurrentPrediction = null;
                    _gameService.ResetPredictionFlag();
                    Log($"Ставка RoshanKill отменена (Рошан не убит)");
                }
                else
                {
                    Log($"Не удалось отменить ставку RoshanKill через API");
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка отмены RoshanKill ставки: {ex.Message}");
            }
        }
        // ================= DonationAlerts =================
        private async Task AuthorizeDonationAlertsViaOAuthAsync()
        {
            _config.DonationAlertsClientId = (DonationAlertsClientId ?? "").Trim();
            _config.DonationAlertsClientSecret = (DonationAlertsClientSecret ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(DonationAlertsRedirectUri))
                _config.DonationAlertsRedirectUri = DonationAlertsRedirectUri.Trim();
            _config.Save();

            Log("Открываю окно авторизации DonationAlerts...");
            bool ok = await _daAuthService.AuthorizeAsync();
            if (!ok) return;

            DonationAlertsAccessToken = _config.DonationAlertsAccessToken;
            DonationAlertsRefreshToken = _config.DonationAlertsRefreshToken;
            OnPropertyChanged(nameof(DonationAlertsUserId));
            StartDonationAlerts();
        }

        private async Task AuthorizeDonationAlertsAsync()
        {
            _config.DonationAlertsClientId = (DonationAlertsClientId ?? "").Trim();
            _config.DonationAlertsClientSecret = (DonationAlertsClientSecret ?? "").Trim();
            _config.DonationAlertsAccessToken = (DonationAlertsAccessToken ?? "").Trim();
            _config.DonationAlertsRefreshToken = (DonationAlertsRefreshToken ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(DonationAlertsRedirectUri))
                _config.DonationAlertsRedirectUri = DonationAlertsRedirectUri.Trim();
            _config.Save();
            Log("Токены DonationAlerts сохранены (шифрование DPAPI)");

            if (string.IsNullOrWhiteSpace(_config.DonationAlertsAccessToken))
            {
                Log("[!] Access Token DonationAlerts не заполнен");
                return;
            }

            if (string.IsNullOrWhiteSpace(_config.DonationAlertsBroadcasterId))
            {
                Log("User ID не указан - пробую получить через API...");
                var user = await _daAuthService.GetUserAsync();
                if (user != null)
                {
                    _config.DonationAlertsBroadcasterId = user.Id.ToString();
                    OnPropertyChanged(nameof(DonationAlertsUserId));
                    Log($"[DA] {user.Name} (id {user.Id})");
                }
                else
                {
                    Log("[!] Не удалось получить User ID автоматически - впишите его в поле User ID вручную");
                }
            }

            StartDonationAlerts();
        }
        private void StartDonationAlerts()
        {
            if (!_config.DonationAlertsEnabled)
            {
                Log("Интеграция с DonationAlerts выключена в настройках");
                return;
            }
            if (!_daAuthService.HasTokens)
            {
                Log("Сначала авторизуйтесь в DonationAlerts");
                return;
            }
            _daService.Start();
        }
        private void LogoutDonationAlerts()
        {
            _daService.Stop();
            _daAuthService.ClearTokens();
            IsDonationAlertsConnected = false;
            DonationAlertsStatus = "Не подключено";
        }
        private async Task HandleDonationAsync(Models.DaDonation donation)
        {
            try
            {
                await _musicService.HandleDonationAsync(donation);
            }
            catch (Exception ex)
            {
                Log($"Ошибка обработки доната: {ex.Message}");
            }
        }
        private async Task AddTrackManuallyAsync()
        {
            string query = (ManualTrackQuery ?? "").Trim();
            if (string.IsNullOrWhiteSpace(query))
                return;
            ManualTrackQuery = "";
            await _musicService.EnqueueAsync(query, "стример", MinDonationAmountRub);
        }
        public void ShutdownServices()
        {
            try
            {
                _daService?.Stop();
                _musicService?.StopPlayback();
            }
            catch { }
        }
        private async Task GetTokenViaOAuthAsync()
        {
            Log("Открываю окно авторизации Twitch...");
            var tokenService = new TwitchTokenService(_config);
            string token = await tokenService.GetAccessTokenAsync();
            if (!string.IsNullOrEmpty(token))
            {
                Log($"Токен успешно получен и сохранён!");
                AccessToken = token;
                if (IsConnected)
                {
                    Log("Переподключаемся к Twitch с новым токеном...");
                    await ConnectToTwitchAsync();
                }
            }
            else
            {
                Log("Не удалось получить токен. Авторизация отменена или произошла ошибка.");
            }
        }
    }
}
