using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TwitchBetBot.Converters
{
    // Конвертер для показа/скрытия элементов на основе наличия данных
    // Например, показываем блок с информацией только если есть данные
    public class NullToVisibilityConverter : IValueConverter
    {
        // Если true - инвертирует: показывать когда null, прятать когда не null
        public bool Invert { get; set; }

        // Проверяем наличие данных и возвращаем Visible/Collapsed
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Проверяем, не null ли объект
            bool isNotNull = value != null;
            // Если нужна инверсия - меняем
            bool shouldShow = Invert ? !isNotNull : isNotNull;
            // true = показываем, false = прячем
            return shouldShow ? Visibility.Visible : Visibility.Collapsed;
        }

        // Обратное преобразование не нужно
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}