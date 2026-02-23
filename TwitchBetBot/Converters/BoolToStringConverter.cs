using System;
using System.Globalization;
using System.Windows.Data;

namespace TwitchBetBot.Converters
{
    // Конвертер для замены true/false на понятные человеку слова
    // Например вместо "True" показывать "Подключено", а вместо "False" - "Отключено"
    public class BoolToStringConverter : IValueConverter
    {
        // Текст для значения true 
        public string TrueText { get; set; } = "Да";
        // Текст для значения false 
        public string FalseText { get; set; } = "Нет";

        // Превращает true/false в соответствующий текст
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Если value - это bool и он true - возвращаем TrueText, иначе FalseText
            return value is bool boolValue && boolValue ? TrueText : FalseText;
        }

        // Обратное преобразование не нужно
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}