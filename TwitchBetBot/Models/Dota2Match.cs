using System;

namespace TwitchBetBot.Models
{
    // Класс для хранения информации о матче Dota 2
    // Создается когда игра начинается и живет пока не закончится
    public class Dota2Match
    {
        // ID матча в системе Dota 2
        public string MatchId { get; set; } = "";
        // Название команды Radiant 
        public string RadiantTeam { get; set; } = "Radiant";
        // Название команды Dire 
        public string DireTeam { get; set; } = "Dire";
        // Когда игра началась
        public DateTime StartTime { get; set; }
        // Когда игра закончилась (null если еще идет)
        public DateTime? EndTime { get; set; }
        // Текущий статус (не началась, идет, завершена, отменена)
        public MatchStatus Status { get; set; } = MatchStatus.NotStarted;
        // Кто победил (Radiant, Dire или CANCELED если отмена)
        public string Winner { get; set; } = "";
        // Сколько длилась игра
        public TimeSpan Duration { get; set; }
        // Режим игры (обычная, турбо и т.д.)
        public string GameMode { get; set; } = "Unknown";
        // Рейтинговая ли игра
        public bool IsRanked { get; set; } = false;
    }

    // Возможные статусы матча
    public enum MatchStatus
    {
        NotStarted,   // Еще не началась
        InProgress,   // Сейчас идет
        Completed,    // Закончилась, есть победитель
        Canceled      // Отменилась (дисконнект, лив)
    }
}