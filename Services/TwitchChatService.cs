using System;
using TwitchBetBot.Models;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Events;
namespace TwitchBetBot.Services
{
    public class TwitchChatService
    {
        private System.Timers.Timer _autoMessageTimer;
        private TwitchClient _client;
        private readonly string _botUsername;
        private readonly string _oauthToken;
        private readonly string _channelName;
        private readonly SessionStats _stats;
        private readonly MusicQueueService _musicService;
        public event Action<string> OnLogMessage;
        public TwitchChatService(
            string botUsername,
            string oauthToken,
            string channelName,
            SessionStats stats,
            MusicQueueService musicService = null)
        {
            _botUsername = botUsername;
            _oauthToken = oauthToken;
            _channelName = channelName;
            _stats = stats;
            _musicService = musicService;
        }
        public void Connect()
        {
            try
            {
                Log("Попытка подключения к IRC...");
                var credentials = new ConnectionCredentials(_botUsername, _oauthToken);
                _client = new TwitchClient();
                _client.Initialize(credentials, _channelName);
                _client.OnConnected += OnConnected;
                _client.OnMessageReceived += OnMessageReceived;
                _client.OnJoinedChannel += OnJoinedChannel;
                _client.OnConnectionError += OnConnectionError;
                _client.OnError += OnError;
                _client.Connect();
                Log("_client.Connect() вызван");
                _autoMessageTimer = new System.Timers.Timer(900000); // 900000 мс = 15 минут
                _autoMessageTimer.Elapsed += (s, e) =>
                {
                    SendAutoMessage();
                };
                _autoMessageTimer.AutoReset = true;
                _autoMessageTimer.Start();
            }
            catch (Exception ex)
            {
                Log($"Ошибка подключения чат-бота: {ex.Message}");
            }
        }
        private void SendAutoMessage()
        {
            try
            {
                string message = "Команды: !mmr - MMR, !wl - Win/Lose, !stats - статистика, !music - текущий трек, !queue - очередь, !help - помощь. Заказ музыки — донат от 100 ₽ с текстом \"!play ютуб-ссылка или название трека\"";
                _client.SendMessage(_channelName, message);
                Log($"Авто-сообщение отправлено");
            }
            catch (Exception ex)
            {
                Log($"Ошибка отправки авто-сообщения: {ex.Message}");
            }
        }
        public void Disconnect()
        {
            try
            {
                _autoMessageTimer?.Stop();
                _autoMessageTimer?.Dispose();
                _client?.Disconnect();
                Log("Чат-бот отключён");
            }
            catch (Exception ex)
            {
                Log($"Ошибка отключения: {ex.Message}");
            }
        }
        private void OnConnected(object sender, OnConnectedArgs e)
        {
            Log($"Чат-бот подключён как {_botUsername}");
        }
        private void OnJoinedChannel(object sender, OnJoinedChannelArgs e)
        {
            Log($"Бот зашёл в канал {e.Channel}");
            _client.SendMessage(e.Channel, "Бот готов! Команды: !mmr, !wl, !stats, !music, !queue, !help. Заказ музыки — донат от 100 ₽ с текстом \"!play ютуб-ссылка или название трека\"");
        }
        private void OnConnectionError(object sender, OnConnectionErrorArgs e)
        {
            Log($"Ошибка подключения: {e.Error.Message}");
        }
        private void OnError(object sender, OnErrorEventArgs e)
        {
            Log($"Ошибка: {e.Exception.Message}");
        }
        private void OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            string message = e.ChatMessage.Message.Trim().ToLower();
            string username = e.ChatMessage.Username;
            Log($"[{username}]: {message}");
            if (message == "!help")
            {
                HandleHelpCommand(username);
            }
            else if (message == "!music")
            {
                HandleMusicCommand(username);
            }
            else if (message == "!queue" || message == "!q")
            {
                HandleQueueCommand(username);
            }
            else if (message == "!skip")
            {
                bool canSkip = e.ChatMessage.IsBroadcaster || e.ChatMessage.IsModerator;
                HandleSkipCommand(username, canSkip);
            }
            else if (message == "!mmr")
            {
                HandlePtsCommand(username);
            }
            else if (message == "!wl")
            {
                HandleWlCommand(username);
            }
            else if (message == "!stats")
            {
                HandleStatsCommand(username);
            }
        }
        private void HandleHelpCommand(string username)
        {
            string response = $"@{username} Доступные команды: !mmr - текущий MMR, !wl - Win/Lose, !music - текущий трек, !queue - очередь заказов, !stats - подробная статистика. Заказ музыки — донат от {(int)MinAmountRub} ₽ с текстом \"!play юутб-ссылка или название трека\"";
            _client.SendMessage(_channelName, response);
            Log($"Ответ на !help: {response}");
        }
        private void HandlePtsCommand(string username)
        {
            string response;
            if (_stats.CurrentMmr > 0)
            {
                string rank = _stats.RankTitle;
                response = $"@{username} Текущий MMR: {_stats.CurrentMmr} ({rank})";
                if (_stats.LastMmrUpdate > DateTime.MinValue)
                {
                    int hoursAgo = (int)(DateTime.Now - _stats.LastMmrUpdate).TotalHours;
                    if (hoursAgo < 24)
                    {
                        response += $" (обновлено {hoursAgo}ч назад)";
                    }
                }
            }
            else
            {
                response = $"@{username} MMR не установлен.";
            }
            _client.SendMessage(_channelName, response);
            Log($"Ответ на !mmr: {response}");
        }
        /// <summary>Минимальная сумма доната для заказа трека — только для текста подсказки</summary>
        public double MinAmountRub { get; set; } = 100;
        private void HandleMusicCommand(string username)
        {
            if (_musicService == null)
            {
                _client.SendMessage(_channelName, $"@{username} Сейчас ничего не играет");
                return;
            }
            _ = SendMusicInfoAsync(username);
        }

        private async Task SendMusicInfoAsync(string username)
        {
            string musicInfo = await _musicService.GetCurrentTrackInfoAsync();
            _client.SendMessage(_channelName, $"@{username} {musicInfo}");
            Log($"Ответ на !music: {musicInfo}");
        }
        private void HandleQueueCommand(string username)
        {
            string info = _musicService != null
                ? _musicService.GetQueueInfo()
                : "Очередь недоступна";
            _client.SendMessage(_channelName, $"@{username} {info}");
            Log($"Ответ на !queue: {info}");
        }
        private void HandleSkipCommand(string username, bool allowed)
        {
            if (_musicService == null)
                return;
            if (!allowed)
            {
                _client.SendMessage(_channelName, $"@{username} !skip доступен только стримеру и модераторам");
                return;
            }
            _musicService.Skip();
            _client.SendMessage(_channelName, $"@{username} {_musicService.GetCurrentMusicInfo()}");
        }
        private void HandleWlCommand(string username)
        {
            int totalWins = _stats.RankedWins + _stats.UnrankedWins;
            int totalLosses = _stats.RankedLosses + _stats.UnrankedLosses;
            int totalGames = totalWins + totalLosses;
            double winRate = totalGames > 0 ? Math.Round((double)totalWins / totalGames * 100, 1) : 0;
            string response = $"@{username} Статистика сессии: {totalWins}W {totalLosses}L (WR: {winRate}%)";
            _client.SendMessage(_channelName, response);
        }
        private void HandleStatsCommand(string username)
        {
            string duration = $"{(int)_stats.SessionDuration.TotalHours}ч {_stats.SessionDuration.Minutes}м";
            int totalWins = _stats.RankedWins + _stats.UnrankedWins;
            int totalLosses = _stats.RankedLosses + _stats.UnrankedLosses;
            int totalGames = totalWins + totalLosses;
            double winRate = totalGames > 0 ? Math.Round((double)totalWins / totalGames * 100, 1) : 0;
            string response = $"@{username} Сессия: {duration} | Всего: {totalWins}W {totalLosses}L (WR: {winRate}%)";
            if (_stats.CurrentMmr > 0)
            {
                response += $" | MMR: {_stats.CurrentMmr} ({_stats.RankTitle})";
            }
            _client.SendMessage(_channelName, response);
        }
        private void Log(string message)
        {
            OnLogMessage?.Invoke($"[Chat] {message}");
        }
    }
}
