using Dota2GSI;
using Dota2GSI.EventMessages;
using Dota2GSI.Nodes;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TwitchBetBot.Models;
using TwitchBetBot.ViewModels;

namespace TwitchBetBot.Services
{
    public class Dota2GameService : INotifyPropertyChanged
    {
        private readonly AppConfig _config;
        private readonly MainViewModel _viewModel;
        private readonly Dispatcher _dispatcher;
        private GameStateListener _gsl;
        private bool _isListening = false;
        private DateTime? _undefinedStartTime = null;
        private bool _disconnectDetected = false;
        private string _lastGameState = "";
        private bool _predictionCreated = false;
        private bool _teamShowcaseDetected = false;
        private string _lastProcessedMatchId = "";
        private Dota2Match _currentMatch;
        private bool _isInGame;
        private string _playerTeam = ""; // Команда локального игрока (Radiant/Dire)
        private OpenDotaService _openDotaService;
        private bool _isEndingGame = false;

        public SessionStats SessionStats { get; set; }

        public Dota2Match CurrentMatch
        {
            get => _currentMatch;
            private set
            {
                _currentMatch = value;
                OnPropertyChanged();
            }
        }

        public bool IsInGame
        {
            get => _isInGame;
            private set
            {
                _isInGame = value;
                OnPropertyChanged();
            }
        }

        public delegate void GameStartedHandler(object sender, Dota2Match match);
        public delegate void GameEndedHandler(object sender, Dota2Match match);

        public event GameStartedHandler OnGameStarted;
        public event GameEndedHandler OnGameEnded;

        public Dota2GameService(AppConfig config, MainViewModel viewModel, OpenDotaService openDotaService)
        {
            _config = config;
            _viewModel = viewModel;
            _dispatcher = Application.Current.Dispatcher;
            _openDotaService = openDotaService;
        }

        private void RunOnUIThread(Action action)
        {
            if (_dispatcher.CheckAccess())
                action();
            else
                _dispatcher.InvokeAsync(action);
        }

        private void LogToUI(string message, bool showTimestamp = true)
        {
            RunOnUIThread(() => _viewModel.Log(message, showTimestamp));
        }

        public void Start()
        {
            if (_isListening)
            {
                LogToUI("⚠️ GSI уже запущен");
                return;
            }

            try
            {
                LogToUI("🚀 Запуск Dota2 GSI...");

                _gsl = new GameStateListener(_config.GSIPort);
                _gsl.NewGameState += OnNewGameState;
                _gsl.TeamVictory += OnTeamVictory;
                _gsl.GenerateGSIConfigFile("TwitchBetBot");

                if (_gsl.Start())
                {
                    _isListening = true;
                    LogToUI($"✅ Dota2 GSI запущен на порту {_config.GSIPort}");
                }
                else
                {
                    LogToUI("❌ Не удалось запустить GSI");
                }
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Ошибка: {ex.Message}");
            }
        }

        private bool IsReplayMatch(GameState gs)
        {
            try
            {
                if (gs?.Player?.Teams == null) return false;

                int totalPlayers = 0;
                foreach (var team in gs.Player.Teams)
                {
                    totalPlayers += team.Value.Count;
                }

                if (totalPlayers >= 10)
                {
                    LogToUI($"🚫 Обнаружен режим наблюдателя (игроков: {totalPlayers}) - реплей/просмотр");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                LogToUI($"⚠️ Ошибка проверки реплея: {ex.Message}");
                return false;
            }
        }

        public void ResetPredictionFlag()
        {
            LogToUI("🔄 Сброс флага создания ставки для следующей игры");
            _predictionCreated = false;
        }

        private string GetPlayerTeam(GameState gs)
        {
            try
            {
                var teamEnum = gs.Player?.LocalPlayer?.Team;

                if (teamEnum.HasValue)
                {
                    if (teamEnum.Value == PlayerTeam.Radiant)
                    {
                        LogToUI($"🎯 Определена команда игрока: RADIANT");
                        return "Radiant";
                    }
                    if (teamEnum.Value == PlayerTeam.Dire)
                    {
                        LogToUI($"🎯 Определена команда игрока: DIRE");
                        return "Dire";
                    }
                }

                int slot = gs.Player?.LocalPlayer?.PlayerSlot ?? -1;
                if (slot >= 0 && slot <= 4)
                {
                    LogToUI($"🎯 Определена команда игрока по слоту: RADIANT (slot={slot})");
                    return "Radiant";
                }
                if (slot >= 128 && slot <= 132)
                {
                    LogToUI($"🎯 Определена команда игрока по слоту: DIRE (slot={slot})");
                    return "Dire";
                }
            }
            catch (Exception ex)
            {
                LogToUI($"⚠️ Ошибка определения команды: {ex.Message}");
            }

            LogToUI($"⚠️ НЕ УДАЛОСЬ ОПРЕДЕЛИТЬ КОМАНДУ ИГРОКА!");
            return "";
        }

        private void OnNewGameState(GameState gs)
        {
            try
            {
                if (gs?.Map == null)
                {
                    HandleUndefinedState();
                    return;
                }

                var map = gs.Map;
                var gameState = map.GameState.ToString();

                LogToUI($"Состояние: {gameState}", false);

                if (_undefinedStartTime.HasValue && gameState != "Undefined")
                {
                    var undefinedDuration = DateTime.Now - _undefinedStartTime.Value;
                    if (undefinedDuration.TotalSeconds < 180)
                    {
                        LogToUI($"✅ Соединение восстановлено ({undefinedDuration.TotalSeconds:F1} сек.)", false);
                        _undefinedStartTime = null;
                        _disconnectDetected = false;
                    }
                }

                if (gameState == "Undefined" || string.IsNullOrEmpty(gameState))
                {
                    HandleUndefinedState();
                    return;
                }

                _lastGameState = gameState;

                if (gameState.Contains("TEAM_SHOWCASE"))
                {
                    if (IsReplayMatch(gs))
                    {
                        LogToUI("🚫 Реплей - пропускаем создание ставки");
                        _teamShowcaseDetected = true;
                        return;
                    }

                    _teamShowcaseDetected = true;

                    if (!_isInGame && !_predictionCreated)
                    {
                        LogToUI("👥 ПОКАЗ КОМАНД - создаём ставку!");

                        var tempMatch = new Dota2Match
                        {
                            MatchId = map.MatchID.ToString(),
                            RadiantTeam = "Radiant",
                            DireTeam = "Dire",
                            StartTime = DateTime.Now,
                            Status = MatchStatus.NotStarted,
                            Winner = ""
                        };

                        LogToUI("🎲 Создание ставки во время показа команд...");
                        RunOnUIThread(() => OnGameStarted?.Invoke(this, tempMatch));
                        _predictionCreated = true;
                    }
                }
                else if (gameState.Contains("GAME_IN_PROGRESS"))
                {

                    if (!_isInGame)
                    {
                        if (_disconnectDetected)
                        {
                            LogToUI("⚠️ Игнорируем GAME_IN_PROGRESS после отмены", false);
                            return;
                        }

                        _undefinedStartTime = null;
                        _disconnectDetected = false;
                        _isInGame = true;

                        // Определяем команду локального игрока
                        _playerTeam = GetPlayerTeam(gs);
                        LogToUI($"📊 Ты играешь за: {_playerTeam}");
                       

                        CurrentMatch = new Dota2Match
                        {
                            PlayerTeam = _playerTeam,
                            MatchId = map.MatchID.ToString(),
                            RadiantTeam = "Radiant",
                            DireTeam = "Dire",
                            StartTime = DateTime.Now,
                            Status = MatchStatus.InProgress,
                            Winner = "",
                            GameMode = map.CustomGameName ?? ""
                        };
                        _lastProcessedMatchId = "";
                        LogToUI($"🎮 ИГРА НАЧАЛАСЬ!");

                        if (!_predictionCreated)
                        {
                            LogToUI("🎲 Создание ставки при старте игры (не было показа команд)");
                            RunOnUIThread(() => OnGameStarted?.Invoke(this, CurrentMatch));
                            _predictionCreated = true;
                        }
                        else
                        {
                            LogToUI("✅ Ставка уже создана во время показа команд", false);
                        }
                    }
                    else if (_isInGame && CurrentMatch != null)
                    {
                        var timeSpan = TimeSpan.FromSeconds(map.GameTime);
                        LogToUI($"⏰ Игровое время: {timeSpan:mm\\:ss}", false);
                    }
                }
                else if (gameState.Contains("POST_GAME"))
                {
                    if (_isInGame && CurrentMatch != null && !_isEndingGame)
                    {
                        _isEndingGame = true;
                        Task.Delay(2000).ContinueWith(_ =>
                        {
                            _dispatcher.InvokeAsync(async () =>
                            {
                                if (string.IsNullOrEmpty(CurrentMatch.Winner))
                                {
                                    DetermineWinnerByOtherMethods();
                                }
                                await EndGame();
                                _isEndingGame = false;
                            });
                        });
                    }
                }
                else if (gameState.Contains("PRE_GAME"))
                {
                    LogToUI("⏳ Подготовка к игре...", false);
                }
                else if (gameState.Contains("STRATEGY_TIME"))
                {
                    LogToUI("🤔 Стратегическое время...", false);
                }
                else if (gameState.Contains("HERO_SELECTION"))
                {
                    LogToUI("🎭 Выбор героев...", false);
                }
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Ошибка обработки: {ex.Message}");
            }
        }

        private void HandleUndefinedState()
        {
            if (!_isInGame) return;

            if (!_undefinedStartTime.HasValue)
            {
                _undefinedStartTime = DateTime.Now;
                LogToUI("⚠️ Нет данных GSI", false);
            }
            else
            {
                var duration = DateTime.Now - _undefinedStartTime.Value;

                if ((int)duration.TotalSeconds % 30 == 0 && !_disconnectDetected)
                {
                    LogToUI($"⏳ Нет данных: {(int)duration.TotalSeconds} сек", false);
                }

                if (duration.TotalSeconds >= 180 && !_disconnectDetected)
                {
                    _disconnectDetected = true;
                    LogToUI($"❌ ДИСКОННЕКТ ПОДТВЕРЖДЁН ({duration.TotalSeconds:F0} сек)");
                    CancelMatchDueToDisconnect();
                }
            }
        }

        private void CancelMatchDueToDisconnect()
        {
            try
            {
                if (!_isInGame || CurrentMatch == null) return;

                LogToUI("🚫 ИГРА БРОШЕНА - отменяем матч и ставку");

                _isInGame = false;
                _predictionCreated = false;
                _teamShowcaseDetected = false;

                CurrentMatch.Status = MatchStatus.Canceled;
                CurrentMatch.Winner = "CANCELED";
                CurrentMatch.EndTime = DateTime.Now;
                CurrentMatch.Duration = CurrentMatch.EndTime.Value - CurrentMatch.StartTime;

                RunOnUIThread(() => OnGameEnded?.Invoke(this, CurrentMatch));

                CurrentMatch = null;
                _undefinedStartTime = null;
                _disconnectDetected = false;
                _lastGameState = "";
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Ошибка отмены матча: {ex.Message}");
            }
        }

        private void OnTeamVictory(TeamVictory gameEvent)
        {
            try
            {
                if (gameEvent?.Team == null)
                {
                    LogToUI("⚠️ TeamVictory: нет данных о команде");
                    return;
                }

                LogToUI("🏆 Получено событие победы!");

                string winner = "";
                string teamValue = gameEvent.Team.ToString() ?? "";

                if (teamValue.Contains("Radiant", StringComparison.OrdinalIgnoreCase))
                    winner = "Radiant";
                else if (teamValue.Contains("Dire", StringComparison.OrdinalIgnoreCase))
                    winner = "Dire";
                else if (teamValue == "2")
                    winner = "Radiant";
                else if (teamValue == "3")
                    winner = "Dire";
                else
                    winner = teamValue;

                LogToUI($"Победитель: {winner}");

                if (_isInGame && CurrentMatch != null)
                {
                    CurrentMatch.Winner = winner;
                    RunOnUIThread(() => EndGame());
                }
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Ошибка TeamVictory: {ex.Message}");
            }
        }

        private void DetermineWinnerByOtherMethods()
        {
            try
            {
                if (_gsl?.CurrentGameState?.Map == null)
                {
                    LogToUI("⚠️ Нет данных для определения победителя");
                    return;
                }

                var map = _gsl.CurrentGameState.Map;
                LogToUI("⚠️ Использую счёт для определения победителя");

                bool radiantWin = map.RadiantScore > map.DireScore;
                CurrentMatch.Winner = radiantWin ? "Radiant" : "Dire";

                LogToUI($"   Radiant: {map.RadiantScore}, Dire: {map.DireScore} → Победитель: {CurrentMatch.Winner}");
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Ошибка определения победителя: {ex.Message}");
                CurrentMatch.Winner = "Unknown";
            }
        }

        private async Task EndGame()
        {
            if (CurrentMatch == null) return;
            if (CurrentMatch.MatchId == _lastProcessedMatchId) return;
            _lastProcessedMatchId = CurrentMatch.MatchId;

            try
            {
                _isInGame = false;
                _predictionCreated = false;
                _teamShowcaseDetected = false;

                CurrentMatch.EndTime = DateTime.Now;
                CurrentMatch.Status = MatchStatus.Completed;
                CurrentMatch.Duration = CurrentMatch.EndTime.Value - CurrentMatch.StartTime;

                if (string.IsNullOrEmpty(CurrentMatch.Winner))
                {
                    LogToUI("❌ Победитель не определён!");
                    CurrentMatch.Winner = "Unknown";
                }
                else
                {
                    if (SessionStats != null && long.TryParse(CurrentMatch.MatchId, out long matchId))
                    {
                        var (isRanked, _) = await _openDotaService.GetMatchInfo(matchId);

                        if (isRanked)
                        {
                            bool playerWon = CurrentMatch.Winner.Equals(_playerTeam, StringComparison.OrdinalIgnoreCase);

                            LogToUI($"🔍 Рейтинг: Winner={CurrentMatch.Winner}, PlayerTeam={_playerTeam}, Win={playerWon}");

                            if (playerWon)
                            {
                                SessionStats.AddRankedWin();
                                LogToUI($"📊 РЕЙТИНГОВАЯ ПОБЕДА! +25 MMR");
                            }
                            else
                            {
                                SessionStats.AddRankedLoss();
                                LogToUI($"📊 РЕЙТИНГОВОЕ ПОРАЖЕНИЕ! -25 MMR");
                            }
                        }
                        else // нерейтинговая
                        {
                            bool playerWon = CurrentMatch.Winner.Equals(_playerTeam, StringComparison.OrdinalIgnoreCase);

                            LogToUI($"🔍 Нерейтинг: Winner={CurrentMatch.Winner}, PlayerTeam={_playerTeam}, Win={playerWon}");

                            if (playerWon)
                                SessionStats.AddUnrankedWin();
                            else
                                SessionStats.AddUnrankedLoss();

                            LogToUI($"📊 Нерейтинговая игра: {(playerWon ? "ПОБЕДА" : "ПОРАЖЕНИЕ")}");
                        }
                    }
                }

                LogToUI($"🏁 ИГРА ЗАВЕРШЕНА!");
                LogToUI($"⏱️ Длительность: {CurrentMatch.Duration:mm\\:ss}");
                LogToUI($"🏆 Победитель: {CurrentMatch.Winner}");

                OnGameEnded?.Invoke(this, CurrentMatch);
                CurrentMatch = null;
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Ошибка EndGame: {ex.Message}");
            }
        }


        public void Stop()
        {
            if (!_isListening) return;

            _gsl?.Stop();
            _isListening = false;
            _isInGame = false;
            _predictionCreated = false;
            _teamShowcaseDetected = false;
            CurrentMatch = null;
            LogToUI("🛑 GSI остановлен");
        }

        public bool IsConnected() => _isListening;
        public bool IsGameRunning() => _isInGame;

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }
}