using System.Windows;
using System.Windows.Controls;

namespace TwitchBetBot
{
    // Вспомогательный класс для привязки пароля из PasswordBox к свойству в ViewModel
    // Обычный PasswordBox не поддерживает Binding напрямую (из соображений безопасности)
    // Этот класс решает эту проблему
    public static class PasswordBoxHelper
    {
        // Свойство, к которому мы будем привязываться в XAML
        public static readonly DependencyProperty PasswordProperty =
            DependencyProperty.RegisterAttached(
                "Password",
                typeof(string),
                typeof(PasswordBoxHelper),
                new FrameworkPropertyMetadata(string.Empty, OnPasswordPropertyChanged));

        // Свойство для включения/выключения привязки
        public static readonly DependencyProperty AttachProperty =
            DependencyProperty.RegisterAttached(
                "Attach",
                typeof(bool),
                typeof(PasswordBoxHelper),
                new PropertyMetadata(false, Attach));

        // Флаг, чтобы не зациклиться при обновлении
        private static readonly DependencyProperty IsUpdatingProperty =
            DependencyProperty.RegisterAttached(
                "IsUpdating",
                typeof(bool),
                typeof(PasswordBoxHelper));

        // Геттеры и сеттеры для Attach
        public static bool GetAttach(DependencyObject dp)
            => (bool)dp.GetValue(AttachProperty);

        public static void SetAttach(DependencyObject dp, bool value)
            => dp.SetValue(AttachProperty, value);

        // Геттеры и сеттеры для Password
        public static string GetPassword(DependencyObject dp)
            => (string)dp.GetValue(PasswordProperty);

        public static void SetPassword(DependencyObject dp, string value)
            => dp.SetValue(PasswordProperty, value);

        // Геттеры и сеттеры для IsUpdating
        private static bool GetIsUpdating(DependencyObject dp)
            => (bool)dp.GetValue(IsUpdatingProperty);

        private static void SetIsUpdating(DependencyObject dp, bool value)
            => dp.SetValue(IsUpdatingProperty, value);

        // Вызывается когда Attach меняется (включаем/выключаем подписку)
        private static void Attach(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is PasswordBox passwordBox)
            {
                // Отписываемся от старого события
                if ((bool)e.OldValue)
                    passwordBox.PasswordChanged -= PasswordChanged;

                // Подписываемся на новое событие
                if ((bool)e.NewValue)
                    passwordBox.PasswordChanged += PasswordChanged;
            }
        }

        // Вызывается когда меняется привязанное свойство Password
        private static void OnPasswordPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is PasswordBox passwordBox)
            {
                // Временно отписываемся от события, чтобы не создать цикл
                passwordBox.PasswordChanged -= PasswordChanged;

                // Обновляем пароль в PasswordBox
                if (!GetIsUpdating(passwordBox))
                {
                    passwordBox.Password = e.NewValue?.ToString() ?? "";
                }

                // Возвращаем подписку
                passwordBox.PasswordChanged += PasswordChanged;
            }
        }

        // Вызывается когда пользователь меняет пароль в поле ввода
        private static void PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox)
            {
                // Устанавливаем флаг, чтобы не создать цикл
                SetIsUpdating(passwordBox, true);
                // Обновляем привязанное свойство
                SetPassword(passwordBox, passwordBox.Password);
                // Снимаем флаг
                SetIsUpdating(passwordBox, false);
            }
        }
    }
}