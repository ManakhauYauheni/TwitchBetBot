using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using TwitchBetBot.Models;
namespace TwitchBetBot.Services
{
    public class MusicRequest
    {
        public TrackInfo Track { get; set; }
        public string Requester { get; set; } = "";
        public double AmountRub { get; set; }
        public DateTime RequestedAt { get; set; } = DateTime.Now;
        public string Title => Track?.Title ?? "";
        public string Url => Track?.Url ?? "";
        public string DurationText => Track?.DurationText ?? "—";
        public string AmountText => $"{Math.Round(AmountRub)} ₽";
        public string Display => $"{Title} — {Requester} ({AmountText}, {DurationText})";
    }
    /// <summary>
    /// Очередь музыкальных заказов из донатов DonationAlerts.
    /// Донатный трек открывается в отдельной вкладке Chrome (через DevTools Protocol).
    /// Пока играет донатный трек, музыка стримера (YouTube-вкладки Chrome) стоит на паузе;
    /// когда очередь кончается — вкладка закрывается и музыка стримера возобновляется.
    /// </summary>
    public class MusicQueueService
    {
        private readonly AppConfig _config;
        private readonly YtDlpService _ytDlp;
        private readonly CurrencyService _currency;
        private readonly ChromeController _chrome;
        private readonly object _lock = new object();
        private System.Timers.Timer _advanceTimer;
        // ID вкладки Chrome с текущим донатным треком
        private string _currentTabId;
        // targetId YouTube-вкладок стримера, которые мы поставили на паузу
        private readonly List<string> _pausedStreamerTabs = new List<string>();
        public ObservableCollection<MusicRequest> Queue { get; } = new ObservableCollection<MusicRequest>();
        public MusicRequest CurrentTrack { get; private set; }
        public DateTime CurrentTrackStartedAt { get; private set; }
        public event Action<string> OnLogMessage;
        public event Action OnQueueChanged;
        public event Action<MusicRequest> OnTrackStarted;
        public MusicQueueService(AppConfig config, YtDlpService ytDlp, CurrencyService currency, ChromeController chrome)
        {
            _config = config;
            _ytDlp = ytDlp;
            _currency = currency;
            _chrome = chrome;
        }
        // ================= Донаты =================
        /// <summary>Обрабатывает донат: проверяет сумму и команду, ставит трек в очередь.</summary>
        public async Task HandleDonationAsync(DaDonation donation)
        {
            if (donation == null) return;
            string command = string.IsNullOrWhiteSpace(_config.MusicCommand) ? "!play" : _config.MusicCommand.Trim();
            string message = donation.Message?.Trim() ?? "";

            // Команда может стоять в любом месте сообщения: "Счастья здоровья !play track name"
            int commandIndex = message.IndexOf(command, StringComparison.OrdinalIgnoreCase);
            if (commandIndex < 0)
                return;

            string query = message.Substring(commandIndex + command.Length).Trim();
            // Срезаем случайную пунктуацию в конце запроса ("call me." -> "call me")
            query = query.TrimEnd('.', ',', '!', '?', ';', ':', '"', ')', ']');

            if (string.IsNullOrWhiteSpace(query))
            {
                Log($"[!] {donation.DisplayName} прислал {command} без названия трека");
                return;
            }
            double amountRub = await GetAmountInRubAsync(donation);
            if (amountRub <= 0)
            {
                Log($"[!] Донат {donation.Amount} {donation.Currency} не удалось пересчитать в рубли — заказ пропущен");
                return;
            }
            if (amountRub + 0.01 < _config.MinDonationAmountRub)
            {
                Log($"[x] {donation.DisplayName}: {Math.Round(amountRub)} руб меньше минимума {_config.MinDonationAmountRub} руб — трек не заказан");
                return;
            }
            Log($"Заказ от {donation.DisplayName} ({Math.Round(amountRub)} руб): {query}");
            await EnqueueAsync(query, donation.DisplayName, amountRub);
        }
        private async Task<double> GetAmountInRubAsync(DaDonation donation)
        {
            string currency = (donation.Currency ?? "").Trim().ToUpperInvariant();
            if (currency == "RUB" || currency == "RUR")
                return donation.Amount;
            // amount_in_user_currency — сумма в валюте стримера; используем, если она уже в рублях
            if (donation.AmountInUserCurrency.HasValue && donation.AmountInUserCurrency.Value > 0 && currency.Length == 0)
                return donation.AmountInUserCurrency.Value;
            return await _currency.ToRubAsync(donation.Amount, currency);
        }
        // ================= Очередь =================
        /// <summary>Добавляет трек в очередь. Если ничего не играет — сразу запускает.</summary>
        public async Task<bool> EnqueueAsync(string query, string requester, double amountRub)
        {
            var track = await _ytDlp.ResolveAsync(query);
            if (track == null)
            {
                Log($"[x] Трек не найден: {query}");
                return false;
            }
            if (_config.MusicMaxDurationSeconds > 0 &&
                track.DurationSeconds > _config.MusicMaxDurationSeconds)
            {
                Log($"[x] Трек слишком длинный ({track.DurationText}), лимит {_config.MusicMaxDurationSeconds} c: {track.Title}");
                return false;
            }
            var request = new MusicRequest
            {
                Track = track,
                Requester = requester,
                AmountRub = amountRub
            };
            bool startNow;
            lock (_lock)
            {
                startNow = CurrentTrack == null;
            }
            if (startNow)
            {
                await StartTrackAsync(request);
            }
            else
            {
                InvokeOnUi(() => Queue.Add(request));
                OnQueueChanged?.Invoke();
                Log($"[+] В очередь ({Queue.Count}): {track.Title} [{track.DurationText}]");
            }
            return true;
        }
        /// <summary>Пропускает текущий трек и запускает следующий.</summary>
        public void Skip()
        {
            var skipped = CurrentTrack;
            if (skipped != null)
                Log($"Пропуск: {skipped.Title}");
            _ = PlayNextAsync();
        }
        public void PlayNext() => _ = PlayNextAsync();
        public async Task PlayNextAsync()
        {
            StopTimer();
            MusicRequest next = null;
            InvokeOnUi(() =>
            {
                if (Queue.Count > 0)
                {
                    next = Queue[0];
                    Queue.RemoveAt(0);
                }
            });
            await CloseCurrentTabAsync();
            if (next == null)
            {
                lock (_lock) { CurrentTrack = null; }
                OnQueueChanged?.Invoke();
                OnTrackStarted?.Invoke(null);
                Log("Очередь пуста");
                // Очередь кончилась — возвращаем музыку стримера
                await ResumeStreamerMusicAsync();
                return;
            }
            await StartTrackAsync(next);
        }
        public void ClearQueue()
        {
            InvokeOnUi(() => Queue.Clear());
            OnQueueChanged?.Invoke();
            Log("Очередь очищена");
        }
        public async void StopPlayback()
        {
            StopTimer();
            lock (_lock) { CurrentTrack = null; }
            OnTrackStarted?.Invoke(null);
            OnQueueChanged?.Invoke();
            await CloseCurrentTabAsync();
            await ResumeStreamerMusicAsync();
            Log("Воспроизведение остановлено, вкладка донатного трека закрыта");
        }
        public string GetCurrentMusicInfo()
        {
            var current = CurrentTrack;
            if (current == null)
                return "Сейчас ничего не играет";
            int queued = Queue.Count;
            string tail = queued > 0 ? $" | в очереди: {queued}" : "";
            return $"Сейчас играет: {current.Title} — {current.Url} (заказал {current.Requester}){tail}";
        }

        /// <summary>
        /// Информация о том, что играет прямо сейчас: донатный трек, а если его нет —
        /// трек стримера из YouTube-вкладки Chrome (для команды !music).
        /// </summary>
        public async Task<string> GetCurrentTrackInfoAsync()
        {
            var current = CurrentTrack;
            if (current != null)
                return GetCurrentMusicInfo();

            if (_config.PauseStreamerMusic && await _chrome.IsAvailableAsync())
            {
                try
                {
                    var track = await _chrome.GetPlayingYouTubeTrackAsync();
                    if (track != null && !string.IsNullOrEmpty(track.Title))
                        return $"Сейчас у стримера: {track.Title} — {track.Url}";
                }
                catch { }
            }

            return "Сейчас ничего не играет";
        }

        public string GetQueueInfo()
        {
            if (Queue.Count == 0)
                return "Очередь пуста";
            var parts = new List<string>();
            for (int i = 0; i < Math.Min(5, Queue.Count); i++)
                parts.Add($"{i + 1}) {Queue[i].Title}");
            string more = Queue.Count > 5 ? $" ... и ещё {Queue.Count - 5}" : "";
            return "Очередь: " + string.Join(", ", parts) + more;
        }
        // ================= Воспроизведение =================
        private async Task StartTrackAsync(MusicRequest request)
        {
            // Если это первый донатный трек — сначала ставим на паузу музыку стримера
            bool donationWasPlaying;
            lock (_lock) { donationWasPlaying = CurrentTrack != null; }
            if (!donationWasPlaying)
                await PauseStreamerMusicAsync();
            lock (_lock)
            {
                CurrentTrack = request;
                CurrentTrackStartedAt = DateTime.Now;
            }
            _currentTabId = await OpenTrackTabAsync(request.Track.Url);
            if (_currentTabId != null)
                Log($"Играет (вкладка Chrome): {request.Track.Title} [{request.Track.DurationText}] — заказал {request.Requester}");
            else
                Log($"Играет (открыл в браузере по умолчанию): {request.Track.Title} [{request.Track.DurationText}] — заказал {request.Requester}");
            OnTrackStarted?.Invoke(request);
            OnQueueChanged?.Invoke();
            ScheduleAdvance(request);
        }
        /// <summary>Ставит на паузу YouTube-вкладки стримера, где сейчас играет видео.</summary>
        private async Task PauseStreamerMusicAsync()
        {
            if (!_config.PauseStreamerMusic) return;
            if (!await _chrome.IsAvailableAsync())
            {
                Log($"[!] Chrome с режимом отладки не найден (порт {_config.ChromeDebugPort}). Музыка стримера не будет поставлена на паузу.");
                Log($"[i] Запустите Chrome с флагами: chrome.exe --remote-debugging-port={_config.ChromeDebugPort} --user-data-dir=C:\\ChromeDebug");
                return;
            }
            lock (_pausedStreamerTabs) { _pausedStreamerTabs.Clear(); }
            var paused = await _chrome.PauseYouTubeTabsAsync();
            if (paused.Count > 0)
            {
                lock (_pausedStreamerTabs) { _pausedStreamerTabs.AddRange(paused); }
                Log($"Пауза музыки стримера ({paused.Count} вкладка/вкладок)");

                // Заодно защищаем вкладки стримера от рекламы
                if (_config.BlockAds)
                    _ = InstallAdSkipperLaterAsync(paused);
            }
            else
            {
                Log("Играющая YouTube-вкладка не найдена — паузить нечего");
            }
        }
        /// <summary>Возобновляет музыку стримера, если мы её ставили на паузу.</summary>
        private async Task ResumeStreamerMusicAsync()
        {
            List<string> toResume;
            lock (_pausedStreamerTabs)
            {
                if (_pausedStreamerTabs.Count == 0) return;
                toResume = new List<string>(_pausedStreamerTabs);
                _pausedStreamerTabs.Clear();
            }
            if (!await _chrome.IsAvailableAsync())
            {
                Log("[!] Chrome недоступен для отладки — не могу возобновить музыку стримера");
                return;
            }
            // Небольшая пауза, чтобы Chrome успел переключить аудио-фокус после закрытия вкладки
            await Task.Delay(1500);
            await _chrome.ResumeTabsAsync(toResume);
            Log($"Музыка стримера возобновлена ({toResume.Count} вкладка/вкладок)");
        }

        /// <summary>Устанавливает скрипт пропуска рекламы во вкладках через пару секунд после открытия страницы.</summary>
        private async Task InstallAdSkipperLaterAsync(List<string> tabIds)
        {
            try
            {
                await Task.Delay(3000); // даём странице загрузиться
                foreach (var id in tabIds)
                    await _chrome.SkipAdsAsync(id);
            }
            catch { }
        }

        /// <summary>Открывает трек в новой вкладке Chrome. Возвращает targetId или null (fallback — браузер по умолчанию).</summary>
        private async Task<string> OpenTrackTabAsync(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (await _chrome.IsAvailableAsync())
            {
                var tabId = await _chrome.OpenTabAsync(url);
                if (tabId != null)
                {
                    // Автопропуск рекламы на донатном треке
                    if (_config.BlockAds)
                        _ = InstallAdSkipperLaterAsync(new List<string> { tabId });
                    return tabId;
                }
                Log("[!] Не удалось открыть вкладку через DevTools — открываю через браузер по умолчанию");
            }
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log($"[x] Не удалось открыть браузер: {ex.Message}");
            }
            return null;
        }
        /// <summary>Закрывает вкладку с текущим донатным треком (если она была открыта нами).</summary>
        private async Task CloseCurrentTabAsync()
        {
            string tabId;
            lock (_lock)
            {
                tabId = _currentTabId;
                _currentTabId = null;
            }
            if (tabId == null) return;
            if (await _chrome.IsAvailableAsync())
            {
                var closed = await _chrome.CloseTabAsync(tabId);
                if (closed)
                    Log("Вкладка с донатным треком закрыта");
                else
                    Log("[!] Не удалось закрыть вкладку (возможно, её закрыли вручную)");
            }
        }
        private void ScheduleAdvance(MusicRequest request)
        {
            StopTimer();
            if (!_config.MusicAutoAdvance)
                return;
            int seconds = request.Track.DurationSeconds;
            if (seconds <= 0)
            {
                Log("[i] Длительность неизвестна — автопереход отключён для этого трека, используйте !skip");
                return;
            }
            // Небольшой запас на загрузку страницы и рекламу
            double interval = (seconds + 8) * 1000.0;
            _advanceTimer = new System.Timers.Timer(interval) { AutoReset = false };
            _advanceTimer.Elapsed += (s, e) =>
            {
                Log("Трек закончился, включаю следующий");
                _ = PlayNextAsync();
            };
            _advanceTimer.Start();
        }
        private void StopTimer()
        {
            try
            {
                _advanceTimer?.Stop();
                _advanceTimer?.Dispose();
            }
            catch { }
            _advanceTimer = null;
        }
        private void InvokeOnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }
        private void Log(string message) => OnLogMessage?.Invoke($"[Music] {message}");
    }
}
