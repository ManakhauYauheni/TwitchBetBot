using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp;
namespace TwitchBetBot.Services
{
    /// <summary>
    /// Управление Google Chrome через DevTools Protocol.
    /// Требует, чтобы Chrome был запущен с флагом --remote-debugging-port=9222.
    /// Умеет: находить YouTube-вкладки, ставить их на паузу и снимать с паузы
    /// (JS в контексте страницы), открывать новую вкладку и закрывать её.
    /// </summary>
    public class ChromeController
    {
        private readonly int _debugPort;
        private readonly HttpClient _http;
        public ChromeController(int debugPort)
        {
            _debugPort = debugPort;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        }
        private string BaseUrl => $"http://127.0.0.1:{_debugPort}";
        public class CdpTarget
        {
            public string Id { get; set; } = "";
            public string Url { get; set; } = "";
            public string Title { get; set; } = "";
            public string Type { get; set; } = "";
            [JsonProperty("webSocketDebuggerUrl")]
            public string WebSocketUrl { get; set; } = "";
        }
        /// <summary>Доступен ли Chrome с режимом отладки.</summary>
        public async Task<bool> IsAvailableAsync()
        {
            try
            {
                var response = await _http.GetAsync($"{BaseUrl}/json/version");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
        /// <summary>Список открытых вкладок.</summary>
        public async Task<List<CdpTarget>> GetTargetsAsync()
        {
            try
            {
                var json = await _http.GetStringAsync($"{BaseUrl}/json/list");
                var targets = JsonConvert.DeserializeObject<List<CdpTarget>>(json) ?? new List<CdpTarget>();
                return targets.Where(t => t.Type == "page").ToList();
            }
            catch
            {
                return new List<CdpTarget>();
            }
        }
        /// <summary>Открывает новую вкладку, возвращает её targetId (или null при неудаче).</summary>
        public async Task<string> OpenTabAsync(string url)
        {
            try
            {
                // Современный Chrome требует PUT для /json/new
                using var content = new StringContent("");
                var response = await _http.PutAsync($"{BaseUrl}/json/new?{Uri.EscapeDataString(url)}", content);
                if (!response.IsSuccessStatusCode)
                    return null;
                var json = await response.Content.ReadAsStringAsync();
                var target = JsonConvert.DeserializeObject<CdpTarget>(json);
                return target?.Id;
            }
            catch
            {
                return null;
            }
        }
        /// <summary>Закрывает вкладку по targetId.</summary>
        public async Task<bool> CloseTabAsync(string targetId)
        {
            if (string.IsNullOrEmpty(targetId)) return false;
            try
            {
                var response = await _http.GetAsync($"{BaseUrl}/json/close/{targetId}");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
        /// <summary>Выполняет JS в контексте вкладки, возвращает результат "value" (или null).</summary>
        public async Task<string> EvaluateAsync(string targetId, string js)
        {
            if (string.IsNullOrEmpty(targetId)) return null;
            CdpTarget target;
            try
            {
                target = (await GetTargetsAsync()).FirstOrDefault(t => t.Id == targetId);
            }
            catch
            {
                return null;
            }
            if (target?.WebSocketUrl == null) return null;
            try
            {
                var ws = new WebSocket(target.WebSocketUrl);
                var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                ws.OnMessage += (s, e) =>
                {
                    try
                    {
                        var msg = JObject.Parse(e.Data);
                        if ((int?)msg["id"] == 1)
                            tcs.TrySetResult(msg["result"]?["result"]?["value"]?.ToString());
                    }
                    catch
                    {
                        tcs.TrySetResult(null);
                    }
                };
                ws.OnError += (s, e) => tcs.TrySetResult(null);
                ws.OnClose += (s, e) => tcs.TrySetResult(null);
                ws.Connect();
                var request = JsonConvert.SerializeObject(new
                {
                    id = 1,
                    method = "Runtime.evaluate",
                    @params = new { expression = js, returnByValue = true }
                });
                ws.Send(request);
                var result = await Task.WhenAny(tcs.Task, Task.Delay(5000)).ContinueWith(t =>
                    t.Result == tcs.Task ? tcs.Task.Result : null);
                ws.Close(CloseStatusCode.Normal);
                return result;
            }
            catch
            {
                return null;
            }
        }
        // ===== YouTube-специфика =====
        private const string PauseJs =
            "(function(){var v=document.querySelector('video');if(v&&!v.paused){v.pause();return 'paused';}return 'none';})()";
        private const string PlayJs =
            "(function(){var v=document.querySelector('video');if(v&&v.paused){v.play();return 'playing';}return 'none';})()";

        /// <summary>
        /// Скрипт-скиппер рекламы. Ставится в страницу один раз (защита от повторов через window.__bbbAdBlock),
        /// каждые 400 мс проверяет наличие рекламы и:
        ///  - проматывает рекламный ролик к концу (currentTime = duration);
        ///  - жмёт кнопку "Пропустить";
        ///  - закрывает рекламные баннеры-оверлеи.
        /// </summary>
        private const string AdBlockJs =
            "(function(){" +
            "if(window.__bbbAdBlock)return;window.__bbbAdBlock=true;" +
            "var skip=function(){" +
            "var a=document.querySelector('.ad-showing');if(!a)return;" +
            "var v=document.querySelector('video');" +
            "if(v&&v.duration){try{v.currentTime=v.duration;}catch(e){}}" +
            "var b=document.querySelector('.ytp-ad-skip-button,.ytp-ad-skip-button-modern,.ytp-skip-ad-button,.ytp-ad-skip-ytp-button');" +
            "if(b)b.click();" +
            "var o=document.querySelector('.ytp-ad-overlay-close-button');" +
            "if(o)o.click();" +
            "};" +
            "setInterval(skip,400);})();";

        /// <summary>Устанавливает в указанной вкладке скрипт автопропуска рекламы YouTube.</summary>
        public async Task SkipAdsAsync(string targetId)
        {
            if (string.IsNullOrEmpty(targetId)) return;
            await EvaluateAsync(targetId, AdBlockJs);
        }

        /// <summary>
        /// Ставит на паузу все YouTube-вкладки, где сейчас играет видео.
        /// Возвращает список targetId поставленных на паузу — их надо передать в ResumeTabsAsync.
        /// </summary>
        public async Task<List<string>> PauseYouTubeTabsAsync()
        {
            var pausedIds = new List<string>();
            foreach (var target in await GetTargetsAsync())
            {
                if (!IsYouTubeUrl(target.Url)) continue;
                var result = await EvaluateAsync(target.Id, PauseJs);
                if (result == "paused")
                {
                    pausedIds.Add(target.Id);
                }
            }
            return pausedIds;
        }
        /// <summary>Возобновляет воспроизведение на указанных вкладках.</summary>
        public async Task ResumeTabsAsync(List<string> targetIds)
        {
            if (targetIds == null) return;
            foreach (var id in targetIds)
            {
                await EvaluateAsync(id, PlayJs);
            }
        }
        /// <summary>Трек, играющий в YouTube-вкладке: название и ссылка.</summary>
        public class CdpTrackInfo
        {
            public string Title { get; set; } = "";
            public string Url { get; set; } = "";
        }

        /// <summary>
        /// Возвращает название и ссылку трека, который сейчас играет в YouTube-вкладке
        /// стримера, или null, если ничего не играет.
        /// </summary>
        public async Task<CdpTrackInfo> GetPlayingYouTubeTrackAsync()
        {
            foreach (var target in await GetTargetsAsync())
            {
                if (!IsYouTubeUrl(target.Url)) continue;

                var state = await EvaluateAsync(target.Id,
                    "(function(){var v=document.querySelector('video');if(v&&!v.paused&&!v.ended)return 'playing';return 'none';})()");

                if (state == "playing")
                {
                    return new CdpTrackInfo
                    {
                        Title = CleanYouTubeTitle(target.Title),
                        Url = CleanYouTubeUrl(target.Url)
                    };
                }
            }

            return null;
        }

        /// <summary>Убирает суффиксы " - YouTube" и " - YouTube Music" из заголовка вкладки.</summary>
        private static string CleanYouTubeTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";
            return title
                .Replace(" - YouTube Music", "", StringComparison.OrdinalIgnoreCase)
                .Replace(" - YouTube", "", StringComparison.OrdinalIgnoreCase)
                .Trim();
        }

        /// <summary>Оставляет в ссылке только идентификатор видео (срезает плейлисты и прочие параметры).</summary>
        private static string CleanYouTubeUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            var match = System.Text.RegularExpressions.Regex.Match(url, @"(?:watch\?v=|youtu\.be/)([\w-]{11})");
            if (match.Success)
                return $"https://www.youtube.com/watch?v={match.Groups[1].Value}";
            return url;
        }

        private static bool IsYouTubeUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return url.Contains("youtube.com/", StringComparison.OrdinalIgnoreCase)
                || url.Contains("music.youtube.com", StringComparison.OrdinalIgnoreCase)
                || url.Contains("youtu.be/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
