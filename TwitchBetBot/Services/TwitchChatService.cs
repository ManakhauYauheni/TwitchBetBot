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

        public event Action<string> OnLogMessage;

        public TwitchChatService(
            string botUsername,
            string oauthToken,
            string channelName,
            SessionStats stats)
        {
            _botUsername = botUsername;
            _oauthToken = oauthToken;
            _channelName = channelName;
            _stats = stats;
        }

        public void Connect()
        {
            try
            {
                Log("🔌 Попытка подключения к IRC...");

                var credentials = new ConnectionCredentials(_botUsername, _oauthToken);
                _client = new TwitchClient();
                _client.Initialize(credentials, _channelName);

                _client.OnConnected += OnConnected;
                _client.OnMessageReceived += OnMessageReceived;
                _client.OnJoinedChannel += OnJoinedChannel;
                _client.OnConnectionError += OnConnectionError;
                _client.OnError += OnError;

                _client.Connect();
                Log("✅ _client.Connect() вызван");
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
                Log($"❌ Ошибка подключения чат-бота: {ex.Message}");
            }
        }


        private void SendAutoMessage()
        {
            try
            {
                string message = "Команды: !pts - MMR, !wl - Win/Lose, !stats - подробная статистика, !music - текущий трек, !help - помощь";
                _client.SendMessage(_channelName, message);
                Log($"🤖 Авто-сообщение отправлено");
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка отправки авто-сообщения: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            try
            {
                _autoMessageTimer?.Stop();
                _autoMessageTimer?.Dispose();
                _client?.Disconnect();
                Log("🛑 Чат-бот отключён");
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка отключения: {ex.Message}");
            }
        }

        private void OnConnected(object sender, OnConnectedArgs e)
        {
            Log($"✅ Чат-бот подключён как {_botUsername}");
        }

        private void OnJoinedChannel(object sender, OnJoinedChannelArgs e)
        {
            Log($"✅ Бот зашёл в канал {e.Channel}");
            _client.SendMessage(e.Channel, "🤖 Бот готов! Команды: !pts, !wl, !stats, !music, !help");
        }

        private void OnConnectionError(object sender, OnConnectionErrorArgs e)
        {
            Log($"❌ Ошибка подключения: {e.Error.Message}");
        }

        private void OnError(object sender, OnErrorEventArgs e)
        {
            Log($"⚠️ Ошибка: {e.Exception.Message}");
        }

        private void OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            string message = e.ChatMessage.Message.Trim().ToLower();
            string username = e.ChatMessage.Username;

            Log($"📨 [{username}]: {message}");

            if (message == "!help")
            {
                HandleHelpCommand(username);
            }
            else if (message == "!music")
            {
                HandleMusicCommand(username);
            }
            else if (message == "!pts")
            {
                HandlePtsCommand(username);
            }
            else if (message == "!wl")
            {
                HandleWnlsCommand(username);
            }
            else if (message == "!stats")
            {
                HandleStatsCommand(username);
            }
        }

        private void HandleHelpCommand(string username)
        {
            string response = $"@{username} Доступные команды: !pts - текущий MMR, !wl - Win/Lose, !music - текущий трек, !stats - подробная статистика";
            _client.SendMessage(_channelName, response);
            Log($"🤖 Ответ на !help: {response}");
        }

        private void HandlePtsCommand(string username)
        {
            string response;

            if (_stats.CurrentMmr > 0)
            {
                string rank = _stats.RankTitle;
                response = $"@{username} 🏆 Текущий MMR: {_stats.CurrentMmr} ({rank})";

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
                response = $"@{username} ℹ️ MMR не установлен.";
            }

            _client.SendMessage(_channelName, response);
            Log($"🤖 Ответ на !pts: {response}");
        }

        private void HandleMusicCommand(string username)
        {
            string musicInfo = "🎵 Сейчас ничего не играет";

           
            if (Views.MainWindow.Instance != null)
            {
                musicInfo = Views.MainWindow.Instance.GetCurrentMusicInfo();
            }

            _client.SendMessage(_channelName, $"@{username} {musicInfo}");
            Log($"🤖 Ответ на !music: {musicInfo}");
        }


        private void HandleWnlsCommand(string username)
        {
            int totalRanked = _stats.RankedWins + _stats.RankedLosses;
            string response = $"@{username} 📊 Статистика сессии:";

            if (totalRanked > 0)
            {
                response += $" Рейтинговые: {_stats.RankedWins}W {_stats.RankedLosses}L (WR: {_stats.RankedWinRate}%)";
            }
            else
            {
                response += $" Рейтинговых игр не было";
            }

            if (_stats.UnrankedWins + _stats.UnrankedLosses > 0)
            {
                response += $" | Нерейтинговые: {_stats.UnrankedWins}W {_stats.UnrankedLosses}L";
            }

            _client.SendMessage(_channelName, response);
            Log($"🤖 Ответ на !wnls: {response}");
        }
        
        private void HandleStatsCommand(string username)
        {
            string duration = $"{(int)_stats.SessionDuration.TotalHours}ч {_stats.SessionDuration.Minutes}м";
            string response = $"@{username} 📈 Сессия: {duration}";

            int totalRanked = _stats.RankedWins + _stats.RankedLosses;
            if (totalRanked > 0)
            {
                response += $"\nРейтинговые: {_stats.RankedWins}W {_stats.RankedLosses}L (WR: {_stats.RankedWinRate}%)";
            }
            else
            {
                response += $"\nРейтинговых игр не было";
            }

            if (_stats.UnrankedWins + _stats.UnrankedLosses > 0)
            {
                response += $"\nНерейтинговые: {_stats.UnrankedWins}W {_stats.UnrankedLosses}L";
            }

            if (_stats.CurrentMmr > 0)
            {
                response += $"\nMMR: {_stats.CurrentMmr} ({_stats.RankTitle})";
            }

      
            if (response.Length > 500)
            {
                string[] parts = response.Split('\n');
                foreach (string part in parts)
                {
                    _client.SendMessage(_channelName, $"@{username} {part}");
                }
            }
            else
            {
                _client.SendMessage(_channelName, response);
            }

            Log($"🤖 Ответ на !stats отправлен");
        }

        private void Log(string message)
        {
            OnLogMessage?.Invoke($"[Chat] {message}");
        }
    }
}