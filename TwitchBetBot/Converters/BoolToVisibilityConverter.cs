using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TwitchBetBot.Converters
{
    // Конвертер для показа/скрытия элементов интерфейса
    // Используется когда нужно спрятать или показать элемент в зависимости от условия
    public class BoolToVisibilityConverter : IValueConverter
    {
        // Если true - то инвертирует поведение (true станет Collapsed, false станет Visible)
        public bool Invert { get; set; }

        // Превращает true/false в Visible или Collapsed
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                // Если нужна инверсия - меняем значение на противоположное
                boolValue = Invert ? !boolValue : boolValue;
                // true = показываем, false = прячем
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            // Если value не булевое - по умолчанию прячем
            return Visibility.Collapsed;
        }

        // Обратное преобразование не нужно
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}