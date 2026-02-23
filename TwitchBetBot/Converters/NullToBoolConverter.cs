using System;
using System.Globalization;
using System.Windows.Data;

namespace TwitchBetBot.Converters
{
    // Конвертер проверяет, есть ли данные (не null) и возвращает true/false
    // Например, если есть активная ставка - вернет true, если нет - false
    public class NullToBoolConverter : IValueConverter
    {
        // Если true - инвертирует: null станет true, а не null станет false
        public bool Invert { get; set; }

        // Проверяем, не равен ли объект null
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Проверяем, не null ли объект
            bool isNotNull = value != null;
            // Если нужна инверсия - меняем результат
            return Invert ? !isNotNull : isNotNull;
        }

        // Обратное преобразование не нужно
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}