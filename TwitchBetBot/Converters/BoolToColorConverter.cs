using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TwitchBetBot.Converters
{
    // Конвертер для смены цвета в зависимости от true/false
    // Нужен чтобы красить кнопки в зеленый/красный и т.д.
    public class BoolToColorConverter : IValueConverter
    {
        // На вход получаем булевое значение и параметр с двумя цветами
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // parameter должен быть строкой вида "ЦветДляTrue|ЦветДляFalse"
            if (parameter is string colors)
            {
                var colorArray = colors.Split('|');
                // Проверяем что все данные корректны
                if (colorArray.Length == 2 && value is bool boolValue)
                {
                    // Выбираем нужный цвет
                    var color = boolValue ? colorArray[0] : colorArray[1];
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                }
            }
            // Если что-то не так - серый цвет по умолчанию
            return new SolidColorBrush(Colors.Gray);
        }

        // Обратная конвертация не нужна
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}