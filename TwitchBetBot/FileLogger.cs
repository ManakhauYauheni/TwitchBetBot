using System;
using System.IO;

namespace TwitchBetBot.Utils
{
    public static class FileLogger
    {
        private static readonly string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static readonly string _currentLogFile;
        private static readonly object _lock = new object();

        static FileLogger()
        {
            try
            {
                if (!Directory.Exists(LogDirectory))
                {
                    Directory.CreateDirectory(LogDirectory);
                }

                // Создаём уникальное имя файла для каждого запуска
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _currentLogFile = Path.Combine(LogDirectory, $"log_{timestamp}.txt");

                WriteHeader();
            }
            catch { }
        }

        private static void WriteHeader()
        {
            Write($"=== ЗАПУСК БОТА: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            Write("==================================================");
        }

        public static void Write(string message)
        {
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(_currentLogFile, message + Environment.NewLine);
                }
            }
            catch { }
        }

        public static void WriteLine(string line)
        {
            Write(line);
        }

        // Получить путь к текущему файлу лога (на всякий случай)
        public static string GetCurrentLogPath()
        {
            return _currentLogFile;
        }
    }
}