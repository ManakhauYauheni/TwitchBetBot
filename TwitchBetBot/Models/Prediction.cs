using System;
using System.Collections.Generic;

namespace TwitchBetBot.Models
{
    // Класс для хранения информации о ставке (предсказании) в Twitch
    // Создается когда бот делает ставку, обновляется по мере поступления данных
    public class Prediction
    {
        // ID ставки в системе Twitch
        public string Id { get; set; } = "";
        // ID канала, где создана ставка
        public string BroadcasterId { get; set; } = "";
        // Заголовок ставки 
        public string Title { get; set; } = "";
        // Список вариантов ответа (обычно 2: Radiant и Dire)
        public List<PredictionOutcome> Outcomes { get; set; } = new();
        // Текущий статус ставки
        public PredictionStatus Status { get; set; }
        // Когда ставка была создана
        public DateTime CreatedAt { get; set; }
        // Когда закрыли прием ставок (null если еще открыто)
        public DateTime? LockedAt { get; set; }
        // Когда завершили ставку (определили победителя)
        public DateTime? EndedAt { get; set; }
        // ID победившего варианта
        public string WinningOutcomeId { get; set; } = "";
        // Сколько времени принимали ставки (в секундах)
        public int PredictionWindowSeconds { get; set; } = 300;
        // Сколько всего человек участвовало
        public int TotalParticipants { get; set; }
        // Сколько всего баллов поставили
        public int TotalPoints { get; set; }
    }

    // Вариант ответа в ставке (Radiant или Dire)
    public class PredictionOutcome
    {
        // ID варианта в системе Twitch
        public string Id { get; set; } = "";
        // Название варианта (Radiant/Dire)
        public string Title { get; set; } = "";
        // Цвет варианта в интерфейсе Twitch (BLUE для Radiant, PINK для Dire)
        public string Color { get; set; } = "BLUE";
        // Сколько человек выбрали этот вариант
        public int Users { get; set; }
        // Сколько баллов поставили на этот вариант
        public int ChannelPoints { get; set; }
        // Коэффициент (рассчитывается автоматически Twitch)
        public decimal Odds { get; set; }
    }

    // Статусы ставки
    public enum PredictionStatus
    {
        ACTIVE,     // Прием открыт
        LOCKED,     // Прием закрыт
        RESOLVED,   // Есть победитель, ставка завершена
        CANCELED    // Ставка отменена (баллы возвращены)
    }
}