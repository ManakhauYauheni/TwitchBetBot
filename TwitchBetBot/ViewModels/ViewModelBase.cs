using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TwitchBetBot.ViewModels
{
    // Базовый класс для всех ViewModel
    // Реализует INotifyPropertyChanged - механизм оповещения UI об изменениях
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        // Событие, которое вызывается когда свойство изменилось
        public event PropertyChangedEventHandler PropertyChanged;

        // Вызывает событие об изменении свойства
        // [CallerMemberName] автоматически подставляет имя свойства, откуда вызван метод
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Удобный метод для установки значения поля и оповещения об изменении
        // Возвращает true если значение действительно изменилось
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false; // Значение не изменилось
            field = value;
            OnPropertyChanged(propertyName); // Сообщаем UI об изменении
            return true;
        }
    }
}