using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
namespace TwitchBetBot.Services
{
    /// <summary>
    /// Пересчёт суммы доната в рубли. Курсы тянутся с open.er-api.com (без ключа) и кешируются.
    /// Если сеть недоступна — используются запасные приблизительные курсы.
    /// </summary>
    public class CurrencyService
    {
        private const string RatesUrl = "https://open.er-api.com/v6/latest/RUB";
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private readonly object _lock = new object();
        // Сколько единиц валюты в 1 RUB
        private Dictionary<string, double> _ratesPerRub = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["RUB"] = 1.0,
            ["USD"] = 0.0110,
            ["EUR"] = 0.0100,
            ["BYN"] = 0.0350,
            ["KZT"] = 5.60,
            ["UAH"] = 0.4500,
            ["BRL"] = 0.0600,
            ["TRY"] = 0.4400
        };
        private DateTime _lastUpdateUtc = DateTime.MinValue;
        public event Action<string> OnLogMessage;
        /// <summary>Переводит сумму в рубли. Неизвестная валюта возвращает 0.</summary>
        public async Task<double> ToRubAsync(double amount, string currency)
        {
            if (string.IsNullOrWhiteSpace(currency))
                return amount;
            currency = currency.Trim().ToUpperInvariant();
            if (currency == "RUB" || currency == "RUR")
                return amount;
            await EnsureRatesAsync();
            double rate;
            lock (_lock)
            {
                if (!_ratesPerRub.TryGetValue(currency, out rate) || rate <= 0)
                {
                    OnLogMessage?.Invoke($"[Currency] Неизвестная валюта {currency}, донат не пересчитан");
                    return 0;
                }
            }
            return amount / rate;
        }
        private async Task EnsureRatesAsync()
        {
            lock (_lock)
            {
                if (DateTime.UtcNow - _lastUpdateUtc < CacheLifetime)
                    return;
            }
            try
            {
                var json = await _http.GetStringAsync(RatesUrl);
                var rates = JObject.Parse(json)["rates"] as JObject;
                if (rates != null)
                {
                    var parsed = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    foreach (var pair in rates)
                    {
                        if (double.TryParse(pair.Value?.ToString(),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out double value) && value > 0)
                        {
                            parsed[pair.Key] = value;
                        }
                    }
                    if (parsed.Count > 0)
                    {
                        parsed["RUB"] = 1.0;
                        lock (_lock)
                        {
                            _ratesPerRub = parsed;
                            _lastUpdateUtc = DateTime.UtcNow;
                        }
                        OnLogMessage?.Invoke($"[Currency] Курсы обновлены ({parsed.Count} валют)");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"[Currency] Не удалось обновить курсы: {ex.Message}. Используются запасные.");
            }
            lock (_lock)
            {
                // чтобы не долбить API при каждом донате
                _lastUpdateUtc = DateTime.UtcNow.AddHours(-5);
            }
        }
    }
}
