using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TwitchBetBot.Models;
namespace TwitchBetBot.Services
{
    public class TrackInfo
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public string Uploader { get; set; } = "";
        public int DurationSeconds { get; set; }
        public string DurationText => DurationSeconds > 0
            ? TimeSpan.FromSeconds(DurationSeconds).ToString(DurationSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss")
            : "—";
    }
    /// <summary>
    /// Поиск трека через yt-dlp (YouTube-поиск по тексту или разбор прямой ссылки).
    /// Скачивание не выполняется — нужен только заголовок, длительность и URL.
    /// </summary>
    public class YtDlpService
    {
        private readonly AppConfig _config;
        public event Action<string> OnLogMessage;
        public YtDlpService(AppConfig config)
        {
            _config = config;
        }
        public string ResolveExecutablePath()
        {
            string configured = string.IsNullOrWhiteSpace(_config.YtDlpPath) ? "yt-dlp.exe" : _config.YtDlpPath.Trim();
            if (Path.IsPathRooted(configured) && File.Exists(configured))
                return configured;
            string nearExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configured);
            if (File.Exists(nearExe))
                return nearExe;
            // Останется найти в PATH
            return configured;
        }
        public bool IsAvailable()
        {
            string path = ResolveExecutablePath();
            if (File.Exists(path)) return true;
            try
            {
                var psi = new ProcessStartInfo(path, "--version")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var process = Process.Start(psi))
                {
                    if (process == null) return false;
                    process.WaitForExit(5000);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
        /// <summary>
        /// Находит трек по названию или ссылке. Возвращает null, если ничего не нашлось.
        /// </summary>
        public async Task<TrackInfo> ResolveAsync(string query, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;
            query = query.Trim();
            bool isUrl = query.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         query.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            string target = isUrl ? query : "ytsearch1:" + query;
            string arguments = $"--no-playlist --no-warnings --skip-download --ignore-config -J \"{target.Replace("\"", "\\\"")}\"";
            string json = await RunAsync(arguments, token);
            if (string.IsNullOrWhiteSpace(json))
                return null;
            try
            {
                var root = JObject.Parse(json);
                // Поисковый запрос возвращает playlist с entries
                if (root["_type"]?.ToString() == "playlist" || root["entries"] != null)
                {
                    var entries = root["entries"] as JArray;
                    if (entries == null || entries.Count == 0)
                        return null;
                    root = entries[0] as JObject;
                }
                if (root == null)
                    return null;
                string id = root["id"]?.ToString() ?? "";
                string url = root["webpage_url"]?.ToString();
                if (string.IsNullOrEmpty(url))
                    url = string.IsNullOrEmpty(id) ? query : $"https://www.youtube.com/watch?v={id}";
                int duration = 0;
                if (double.TryParse(root["duration"]?.ToString(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double d))
                {
                    duration = (int)Math.Round(d);
                }
                return new TrackInfo
                {
                    Id = id,
                    Title = root["title"]?.ToString() ?? query,
                    Url = url,
                    Uploader = root["uploader"]?.ToString() ?? "",
                    DurationSeconds = duration
                };
            }
            catch (Exception ex)
            {
                Log($"Не удалось разобрать ответ yt-dlp: {ex.Message}");
                return null;
            }
        }
        private async Task<string> RunAsync(string arguments, CancellationToken token)
        {
            string exe = ResolveExecutablePath();
            var psi = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            };
            try
            {
                using (var process = new Process { StartInfo = psi, EnableRaisingEvents = true })
                {
                    var stdout = new StringBuilder();
                    var stderr = new StringBuilder();
                    var exited = new TaskCompletionSource<bool>();
                    process.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
                    process.Exited += (s, e) => exited.TrySetResult(true);
                    if (!process.Start())
                    {
                        Log("Не удалось запустить yt-dlp");
                        return null;
                    }
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    var timeout = Task.Delay(TimeSpan.FromSeconds(60), token);
                    var finished = await Task.WhenAny(exited.Task, timeout);
                    if (finished == timeout)
                    {
                        try { process.Kill(true); } catch { }
                        Log("yt-dlp не ответил за 60 секунд");
                        return null;
                    }
                    if (process.ExitCode != 0)
                    {
                        Log($"yt-dlp вернул код {process.ExitCode}: {stderr.ToString().Trim()}");
                        return null;
                    }
                    return stdout.ToString();
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Log($"yt-dlp не найден ({exe}). Укажите путь в настройках или положите yt-dlp.exe рядом с программой.");
                return null;
            }
            catch (Exception ex)
            {
                Log($"Ошибка запуска yt-dlp: {ex.Message}");
                return null;
            }
        }
        private void Log(string message) => OnLogMessage?.Invoke($"[yt-dlp] {message}");
    }
}
