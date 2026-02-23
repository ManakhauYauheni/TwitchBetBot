using Dota2GSI;
using Dota2GSI.EventMessages;
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
    // Сервис для получения данных из Dota 2 через Game State Integration (GSI)
    // GSI - это когда Dota 2 отправляет HTTP запросы с данными о матче
    // Мы запускаем сервер, Dota 2 стучится к нам, а мы обрабатываем события
    public class Dota2GameService : INotifyPropertyChanged
    {
        // ========== Приватные поля ==========

        private readonly AppConfig _config;              // Настройки (порт и т.д.)
        private readonly MainViewModel _viewModel;       // Чтобы писать в лог
        private readonly Dispatcher _dispatcher;         // Для работы с UI потоком
        private GameStateListener _gsl;                  // Сам слушатель GSI (библиотека Dota2GSI)
        private bool _isListening = false;                // Запущен ли слушатель
        private DateTime? _undefinedStartTime = null;     // Когда начались проблемы с соединением
        private bool _disconnectDetected = false;         // Зафиксирован ли дисконнект
        private string _lastGameState = "";                // Последнее состояние игры
        private bool _predictionCreated = false;           // Уже создали ставку для этой игры?
        private bool _teamShowcaseDetected = false;        // Был ли показ команд?

        private Dota2Match _currentMatch;                  // Текущий матч
        private bool _isInGame;                             // Идет ли игра сейчас

        // ========== Свойства ==========

        // Текущий матч (чтобы показывать в интерфейсе)
        public Dota2Match CurrentMatch
        {
            get => _currentMatch;
            private set
            {
                _currentMatch = value;
                OnPropertyChanged(); // Уведомляем UI об изменении
            }
        }

        // Флаг "идет игра"
        public bool IsInGame
        {
            get => _isInGame;
            private set
            {
                _isInGame = value;
                OnPropertyChanged();
            }
        }

        // ========== События ==========

        // Эти события вызываются когда игра начинается или заканчивается
        // MainViewModel подписывается на них чтобы создавать/завершать ставки
        public delegate void GameStartedHandler(object sender, Dota2Match match);
        public delegate void GameEndedHandler(object sender, Dota2Match match);

        public event GameStartedHandler OnGameStarted;  // Игра началась
        public event GameEndedHandler OnGameEnded;      // Игра закончилась

        // ========== Конструктор ==========

        public Dota2GameService(AppConfig config, MainViewModel viewModel)
        {
            _config = config;
            _viewModel = viewModel;
            _dispatcher = Application.Current.Dispatcher; // Запоминаем UI поток
        }

        // ========== Вспомогательные методы для работы с UI потоком ==========

        // Выполняет действие в UI потоке (нужно чтобы не было ошибок при обновлении интерфейса)
        private void RunOnUIThread(Action action)
        {
            if (_dispatcher.CheckAccess())
                action(); // Мы уже в UI потоке
            else
                _dispatcher.InvokeAsync(action); // Переключаемся в UI поток
        }

        // Логирует сообщение через MainViewModel (всегда в UI потоке)
        private void LogToUI(string message, bool showTimestamp = true)
        {
            RunOnUIThread(() => _viewModel.Log(message, showTimestamp));
        }

        // ========== Запуск и остановка GSI ==========

        // Запускаем GSI сервер
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

                // Создаем слушатель на указанном порту
                _gsl = new GameStateListener(_config.GSIPort);

                // Подписываемся на события
                _gsl.NewGameState += OnNewGameState;      // Приходит каждую секунду
                _gsl.TeamVictory += OnTeamVictory;        // Когда какая-то команда победила

                // Создаем конфиг файл для Dota 2 (чтобы игра знала куда стучаться)
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

        // Останавливаем GSI сервер
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

        // Проверка статуса
        public bool IsConnected() => _isListening;
        public bool IsGameRunning() => _isInGame;
        // ========== Обработка новых данных от игры ==========

        // Это метод вызывается каждый раз, когда Dota 2 присылает новое состояние
        // (примерно раз в секунду во время игры)
        private void OnNewGameState(GameState gs)
        {
            try
            {
                // Если нет данных о карте - значит что-то пошло не так
                if (gs?.Map == null)
                {
                    HandleUndefinedState();
                    return;
                }

                var map = gs.Map;
                var gameState = map.GameState.ToString(); // Текущее состояние игры

                LogToUI($"Состояние: {gameState}", false);

                // === Проверяем, не восстановилось ли соединение после проблем ===
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

                // === Состояние Undefined (нет данных) ===
                if (gameState == "Undefined" || string.IsNullOrEmpty(gameState))
                {
                    HandleUndefinedState();
                    return;
                }

                _lastGameState = gameState;

                // === ПОКАЗ КОМАНД ===
                // Это состояние бывает перед началом игры, показывают команды
                if (gameState.Contains("TEAM_SHOWCASE"))
                {
                    _teamShowcaseDetected = true;

                    // Если игра еще не началась и ставка еще не создана
                    if (!_isInGame && !_predictionCreated)
                    {
                        LogToUI("👥 ПОКАЗ КОМАНД - создаём ставку!");

                        // Создаем временный матч (без команд, только ID)
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

                // === ИГРА НАЧАЛАСЬ ===
                else if (gameState.Contains("GAME_IN_PROGRESS"))
                {
                    if (!_isInGame)
                    {
                        // Если был дисконнект - игнорируем
                        if (_disconnectDetected)
                        {
                            LogToUI("⚠️ Игнорируем GAME_IN_PROGRESS после отмены", false);
                            return;
                        }

                        _undefinedStartTime = null;
                        _disconnectDetected = false;
                        _isInGame = true;

                        // Создаем полноценный матч
                        CurrentMatch = new Dota2Match
                        {
                            MatchId = map.MatchID.ToString(),
                            RadiantTeam = "Radiant",
                            DireTeam = "Dire",
                            StartTime = DateTime.Now,
                            Status = MatchStatus.InProgress,
                            Winner = ""
                        };

                        LogToUI($"🎮 ИГРА НАЧАЛАСЬ!");

                        // Если ставка еще не создана (не было показа команд)
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
                        // Просто показываем игровое время
                        var timeSpan = TimeSpan.FromSeconds(map.GameTime);
                        LogToUI($"⏰ Игровое время: {timeSpan:mm\\:ss}", false);
                    }
                }

                // === КОНЕЦ ИГРЫ ===
                else if (gameState.Contains("POST_GAME"))
                {
                    if (_isInGame && CurrentMatch != null)
                    {
                        // Даем игре 2 секунды на то, чтобы прислать данные о победителе
                        Task.Delay(2000).ContinueWith(_ =>
                        {
                            _dispatcher.InvokeAsync(() =>
                            {
                                if (string.IsNullOrEmpty(CurrentMatch.Winner))
                                {
                                    DetermineWinnerByOtherMethods();
                                }
                                EndGame();
                            });
                        });
                    }
                }

                // === ДРУГИЕ СОСТОЯНИЯ ===
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
        // ========== Обработка проблем с соединением ==========

        // Вызывается когда от игры нет данных (Undefined)
        private void HandleUndefinedState()
        {
            if (!_isInGame) return; // Если игры нет - нам все равно

            if (!_undefinedStartTime.HasValue)
            {
                // Первый раз заметили пропажу данных
                _undefinedStartTime = DateTime.Now;
                LogToUI("⚠️ Нет данных GSI", false);
            }
            else
            {
                var duration = DateTime.Now - _undefinedStartTime.Value;

                // Логируем каждые 30 секунд
                if ((int)duration.TotalSeconds % 30 == 0 && !_disconnectDetected)
                {
                    LogToUI($"⏳ Нет данных: {(int)duration.TotalSeconds} сек", false);
                }

                // Если нет данных больше 3 минут - считаем дисконнектом
                if (duration.TotalSeconds >= 180 && !_disconnectDetected)
                {
                    _disconnectDetected = true;
                    LogToUI($"❌ ДИСКОННЕКТ ПОДТВЕРЖДЁН ({duration.TotalSeconds:F0} сек)");
                    CancelMatchDueToDisconnect();
                }
            }
        }

        // Отмена матча из-за дисконнекта
        private void CancelMatchDueToDisconnect()
        {
            try
            {
                if (!_isInGame || CurrentMatch == null) return;

                LogToUI("🚫 ИГРА БРОШЕНА - отменяем матч и ставку");

                _isInGame = false;
                _predictionCreated = false;
                _teamShowcaseDetected = false;

                // Помечаем матч как отмененный
                CurrentMatch.Status = MatchStatus.Canceled;
                CurrentMatch.Winner = "CANCELED";
                CurrentMatch.EndTime = DateTime.Now;
                CurrentMatch.Duration = CurrentMatch.EndTime.Value - CurrentMatch.StartTime;

                // Вызываем событие окончания игры (MainViewModel отменит ставку)
                RunOnUIThread(() => OnGameEnded?.Invoke(this, CurrentMatch));

                // Очищаем данные
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

        // ========== Обработка победы ==========

        // Событие победы команды (приходит от Dota 2 GSI)
        private void OnTeamVictory(TeamVictory gameEvent)
        {
            try
            {
                // Проверяем что данные есть
                if (gameEvent?.Team == null)
                {
                    LogToUI("⚠️ TeamVictory: нет данных о команде");
                    return;
                }

                LogToUI("🏆 Получено событие победы!");

                string winner = "";
                string teamValue = gameEvent.Team.ToString() ?? "";

                // Определяем победителя (Radiant или Dire)
                if (teamValue.Contains("Radiant", StringComparison.OrdinalIgnoreCase))
                    winner = "Radiant";
                else if (teamValue.Contains("Dire", StringComparison.OrdinalIgnoreCase))
                    winner = "Dire";
                else if (teamValue == "2") // В GSI 2 = Radiant
                    winner = "Radiant";
                else if (teamValue == "3") // 3 = Dire
                    winner = "Dire";
                else
                    winner = teamValue;

                LogToUI($"Победитель: {winner}");

                // Если игра идет - записываем победителя и завершаем
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

        // Если не пришло событие победы - определяем победителя по счету
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

                // Сравниваем счет
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

        // Завершаем игру (вызывается когда матч закончился)
        private void EndGame()
        {
            try
            {
                if (CurrentMatch == null) return;

                _isInGame = false;
                _predictionCreated = false;
                _teamShowcaseDetected = false;

                // Заполняем данные о конце игры
                CurrentMatch.EndTime = DateTime.Now;
                CurrentMatch.Status = MatchStatus.Completed;
                CurrentMatch.Duration = CurrentMatch.EndTime.Value - CurrentMatch.StartTime;

                if (string.IsNullOrEmpty(CurrentMatch.Winner))
                {
                    LogToUI("❌ Победитель не определён!");
                    CurrentMatch.Winner = "Unknown";
                }

                LogToUI($"🏁 ИГРА ЗАВЕРШЕНА!");
                LogToUI($"⏱️ Длительность: {CurrentMatch.Duration:mm\\:ss}");
                LogToUI($"🏆 Победитель: {CurrentMatch.Winner}");

                // Вызываем событие окончания игры
                OnGameEnded?.Invoke(this, CurrentMatch);
                CurrentMatch = null;
            }
            catch (Exception ex)
            {
                LogToUI($"❌ Ошибка EndGame: {ex.Message}");
            }
        }

        // ========== INotifyPropertyChanged ==========

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}