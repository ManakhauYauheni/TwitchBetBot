using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TwitchBetBot.Models;
namespace TwitchBetBot.Services
{
    /// <summary>
    /// Подключение к Centrifugo DonationAlerts и приём донатов в реальном времени.
    /// Канал: $alerts:donation_&lt;user_id&gt;
    /// </summary>
    public class DonationAlertsService
    {
        private const string WsEndpoint = "wss://centrifugo.donationalerts.com/connection/websocket";
        private readonly AppConfig _config;
        private readonly DonationAlertsAuthService _auth;
        private ClientWebSocket _socket;
        private CancellationTokenSource _cts;
        private Task _loopTask;
        private int _commandId;
        private readonly System.Collections.Generic.HashSet<long> _seenDonations = new System.Collections.Generic.HashSet<long>();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        public bool IsRunning { get; private set; }
        public bool IsConnected { get; private set; }
        public string ConnectedAs { get; private set; } = "";
        public event Action<string> OnLogMessage;
        public event Action<DaDonation> OnDonation;
        public event Action<bool> OnConnectionChanged;
        public DonationAlertsService(AppConfig config, DonationAlertsAuthService auth)
        {
            _config = config;
            _auth = auth;
        }
        public void Start()
        {
            if (IsRunning)
            {
                Log("Уже запущено");
                return;
            }
            if (!_auth.HasTokens)
            {
                Log("Нет токена DonationAlerts. Сначала авторизуйтесь.");
                return;
            }
            IsRunning = true;
            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
            Log("Запуск слушателя донатов...");
        }
        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;
            try { _cts?.Cancel(); } catch { }
            try
            {
                _socket?.Abort();
                _socket?.Dispose();
            }
            catch { }
            _socket = null;
            SetConnected(false);
            Log("Слушатель донатов остановлен");
        }
        private async Task RunLoopAsync(CancellationToken token)
        {
            int attempt = 0;
            while (!token.IsCancellationRequested && IsRunning)
            {
                try
                {
                    await ConnectAndListenAsync(token);
                    attempt = 0;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"Обрыв соединения: {ex.Message}");
                }
                SetConnected(false);
                if (!IsRunning || token.IsCancellationRequested)
                    break;
                attempt++;
                int delaySeconds = Math.Min(60, 3 * attempt);
                Log($"Переподключение через {delaySeconds} c...");
                try { await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token); }
                catch (OperationCanceledException) { break; }
            }
        }
        private async Task ConnectAndListenAsync(CancellationToken token)
        {
            await _auth.EnsureValidTokenAsync();
            var user = await _auth.GetUserAsync();
            if (user == null || string.IsNullOrEmpty(user.SocketConnectionToken))
                throw new Exception("не получен socket_connection_token");
            ConnectedAs = user.Name;
            if (_config.DonationAlertsBroadcasterId != user.Id.ToString())
            {
                _config.DonationAlertsBroadcasterId = user.Id.ToString();
                _config.Save();
            }
            _socket = new ClientWebSocket();
            await _socket.ConnectAsync(new Uri(WsEndpoint), token);
            Log("WebSocket Centrifugo открыт");
            // 1. connect
            int connectId = NextId();
            await SendAsync(new JObject
            {
                ["params"] = new JObject { ["token"] = user.SocketConnectionToken },
                ["id"] = connectId
            }, token);
            string clientId = null;
            string channel = $"$alerts:donation_{user.Id}";
            bool subscribed = false;
            var pingTimer = new Timer(async _ =>
            {
                try
                {
                    if (_socket?.State == WebSocketState.Open)
                        await SendAsync(new JObject { ["method"] = 7, ["id"] = NextId() }, CancellationToken.None);
                }
                catch { }
            }, null, TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(25));
            try
            {
                await foreach (var message in ReadMessagesAsync(token))
                {
                    // Ping от сервера — отвечаем пустым объектом
                    if (!message.HasValues)
                    {
                        await SendAsync(new JObject(), token);
                        continue;
                    }
                    var error = message["error"];
                    if (error != null)
                    {
                        string errText = error.ToString(Formatting.None);
                        Log($"Centrifugo error: {errText}");
                        // Токен протух — обновляем и переподключаемся
                        if (errText.Contains("token") || errText.Contains("permission") || errText.Contains("unauthorized"))
                        {
                            await _auth.EnsureValidTokenAsync(force: true);
                            throw new Exception("требуется переподключение после ошибки авторизации");
                        }
                        continue;
                    }
                    var result = message["result"];
                    if (result == null)
                        continue;
                    // Ответ на connect
                    if (clientId == null && result["client"] != null)
                    {
                        clientId = result["client"].ToString();
                        Log($"Centrifugo client: {clientId}");
                        var channels = await _auth.SubscribeChannelsAsync(clientId, channel);
                        if (channels == null || channels.Length == 0)
                            throw new Exception("не удалось подписаться на канал донатов");
                        foreach (var ch in channels)
                        {
                            await SendAsync(new JObject
                            {
                                ["params"] = new JObject
                                {
                                    ["channel"] = ch.Channel,
                                    ["token"] = ch.Token
                                },
                                ["method"] = 1,
                                ["id"] = NextId()
                            }, token);
                        }
                        continue;
                    }
                    // Подтверждение подписки
                    if (!subscribed && result["channel"] != null && result["type"] != null)
                    {
                        subscribed = true;
                        SetConnected(true);
                        Log($"Подписка активна: {result["channel"]}");
                        continue;
                    }
                    // Публикация: result.data.data — сам донат
                    var payload = result["data"]?["data"];
                    if (payload != null && payload.Type == JTokenType.Object)
                    {
                        if (!subscribed)
                        {
                            subscribed = true;
                            SetConnected(true);
                        }
                        HandleDonation(payload as JObject);
                    }
                }
            }
            finally
            {
                pingTimer.Dispose();
            }
        }
        private void HandleDonation(JObject payload)
        {
            try
            {
                var donation = payload.ToObject<DaDonation>();
                if (donation == null) return;
                if (donation.Id != 0 && !_seenDonations.Add(donation.Id))
                    return; // дубликат
                if (_seenDonations.Count > 500)
                    _seenDonations.Clear();
                Log($"Донат от {donation.DisplayName}: {donation.Amount} {donation.Currency} — {donation.Message}");
                OnDonation?.Invoke(donation);
            }
            catch (Exception ex)
            {
                Log($"Не удалось разобрать донат: {ex.Message}");
            }
        }
        private async System.Collections.Generic.IAsyncEnumerable<JObject> ReadMessagesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            var buffer = new byte[8192];
            while (_socket != null && _socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var sb = new StringBuilder();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                        yield break;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);
                string raw = sb.ToString().Trim();
                if (raw.Length == 0)
                    continue;
                // В одном фрейме может прийти несколько JSON-объектов подряд
                using (var reader = new JsonTextReader(new StringReader(raw)) { SupportMultipleContent = true })
                {
                    while (true)
                    {
                        JObject obj = null;
                        try
                        {
                            if (!reader.Read()) break;
                            obj = JObject.Load(reader);
                        }
                        catch (Exception ex)
                        {
                            Log($"Некорректный JSON от Centrifugo: {ex.Message}");
                            break;
                        }
                        if (obj != null)
                            yield return obj;
                    }
                }
            }
        }
        private async Task SendAsync(JObject message, CancellationToken token)
        {
            if (_socket == null || _socket.State != WebSocketState.Open)
                return;
            var bytes = Encoding.UTF8.GetBytes(message.ToString(Formatting.None));
            await _sendLock.WaitAsync(token);
            try
            {
                await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
            }
            finally
            {
                _sendLock.Release();
            }
        }
        private int NextId() => Interlocked.Increment(ref _commandId);
        private void SetConnected(bool value)
        {
            if (IsConnected == value) return;
            IsConnected = value;
            OnConnectionChanged?.Invoke(value);
        }
        private void Log(string message) => OnLogMessage?.Invoke($"[DA] {message}");
    }
}
