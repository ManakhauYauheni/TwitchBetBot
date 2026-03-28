using System;

namespace TwitchBetBot.Models
{
    // Класс для хранения информации о матче Dota 2
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

        // Текущий статус
        public MatchStatus Status { get; set; } = MatchStatus.NotStarted;

        // Кто победил (Radiant, Dire или CANCELED)
        public string Winner { get; set; } = "";

        // Сколько длилась игра
        public TimeSpan Duration { get; set; }

        // Режим игры
        public string GameMode { get; set; } = "Unknown";

      
       
    }

    // Возможные статусы матча
    public enum MatchStatus
    {
        NotStarted,   // Еще не началась
        InProgress,   // Сейчас идет
        Completed,    // Закончилась
        Canceled      // Отменилась (дисконнект)
    }
}