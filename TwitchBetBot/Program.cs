using System;
using System.Windows;

namespace TwitchBetBot
{
    // Точка входа в программу
    // Запускает WPF приложение
    public class Program
    {
        // STAThread - требуется для WPF приложений (однопоточная модель)
        [STAThread]
        public static void Main()
        {
            try
            {
                // Создаем экземпляр приложения
                var app = new App();
                // Инициализируем компоненты (ресурсы, стили)
                app.InitializeComponent();
                // Запускаем приложение (открывается главное окно)
                app.Run();
            }
            catch (Exception ex)
            {
                // Если что-то пошло совсем плохо - показываем сообщение
                MessageBox.Show($"Ошибка запуска: {ex.Message}\n\n{ex.StackTrace}",
                    "Критическая ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}