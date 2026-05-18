using System;

namespace TwitchBetBot.Models
{
    
    // Класс для хранения статистики текущей сессии

    public class SessionStats
    {
        // ========== Свойства ==========

       
        // Количество рейтинговых побед
        
        public int RankedWins { get; set; } = 0;

       
        // Количество рейтинговых поражений
       
        public int RankedLosses { get; set; } = 0;

        
        // Количество нерейтинговых побед
        
        public int UnrankedWins { get; set; } = 0;

       
        // Количество нерейтинговых поражений
        
        public int UnrankedLosses { get; set; } = 0;

        
        // Текущий ММР
   
        public int CurrentMmr { get; set; } = 0;

        
        // Время последнего обновления ММР
       
        public DateTime LastMmrUpdate { get; set; } = DateTime.MinValue;

        // Время начала сессии
      
        public DateTime SessionStartTime { get; set; } = DateTime.Now;

        // ========== Методы ==========

        
        // Добавить рейтинговую победу
       
        public void AddRankedWin()
        {
            RankedWins++;
            CurrentMmr += 25;
            LastMmrUpdate = DateTime.Now;
        }

        
        // Добавить рейтинговое поражение
 
        public void AddRankedLoss()
        {
            RankedLosses++;
            CurrentMmr -= 25;
            LastMmrUpdate = DateTime.Now;
        }

        // Добавить нерейтинговую победу
     
        public void AddUnrankedWin()
        {
            UnrankedWins++;
        }

        
        // Добавить нерейтинговое поражение
    
        public void AddUnrankedLoss()
        {
            UnrankedLosses++;
        }

        
        // Установить ММР вручную (из конфига)
       
        public void SetMmr(int mmr)
        {
            CurrentMmr = mmr;
            LastMmrUpdate = DateTime.Now;
        }

       
        // Сбросить статистику сессии
       
        public void Reset()
        {
            RankedWins = 0;
            RankedLosses = 0;
            UnrankedWins = 0;
            UnrankedLosses = 0;
            SessionStartTime = DateTime.Now;
        }

     
        // Процент побед в рейтинговых играх
     
        public double RankedWinRate
        {
            get
            {
                if (RankedWins + RankedLosses == 0) return 0;
                return Math.Round((double)RankedWins / (RankedWins + RankedLosses) * 100, 1);
            }
        }

       
        // Общее количество сыгранных игр
      
        public int TotalGames => RankedWins + RankedLosses + UnrankedWins + UnrankedLosses;

        
        // Длительность сессии
     
        public TimeSpan SessionDuration => DateTime.Now - SessionStartTime;

       
        // Ранг на основе ММР
   
        public string RankTitle
        {
            get
            {
                if (CurrentMmr == 0) return "Не определён";
                if (CurrentMmr < 770) return "Herald";
                if (CurrentMmr < 1540) return "Guardian";
                if (CurrentMmr < 2310) return "Crusader";
                if (CurrentMmr < 3080) return "Archon";
                if (CurrentMmr < 3850) return "Legend";
                if (CurrentMmr < 4620) return "Ancient";
                if (CurrentMmr < 5420) return "Divine";
                return "Immortal";
            }
        }
    }
}