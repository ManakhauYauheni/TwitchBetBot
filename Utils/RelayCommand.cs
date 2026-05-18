using System;
using System.Windows.Input;

namespace TwitchBetBot.Utils
{
    // Класс для создания команд в MVVM паттерне
    // Позволяет привязать методы из ViewModel к кнопкам в XAML
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;        // Что делать при выполнении команды
        private readonly Func<bool> _canExecute; // Можно ли выполнить команду сейчас

        // Событие, которое сообщает UI о том, что CanExecute изменился
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        // Конструктор - принимает метод для выполнения и опционально метод проверки
        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        // Проверяет, можно ли выполнить команду
        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute();
        }

        // Выполняет команду
        public void Execute(object parameter)
        {
            _execute();
        }

        // Принудительно обновляет состояние команды (перепроверяет CanExecute)
        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}