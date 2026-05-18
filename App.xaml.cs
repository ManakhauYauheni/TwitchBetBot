using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;

namespace TwitchBetBot
{
    public partial class App : Application
    {
        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);

            // Небольшая задержка перед очисткой
            Thread.Sleep(500);

            CleanEBWebView();
        }

        private void CleanEBWebView()
        {
            try
            {
                string ebWebViewPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "TwitchBetBot.exe.WebView2",
                    "EBWebView");

                if (!Directory.Exists(ebWebViewPath))
                    return;

                // ШАГ 1: Удаляем всё в EBWebView, кроме Default и Local State
                foreach (var item in Directory.GetFileSystemEntries(ebWebViewPath))
                {
                    string name = Path.GetFileName(item);

                    if (name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Local State", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    DeleteWithRetry(item);
                }

                // ШАГ 2: Очищаем папку Default, но сохраняем Network
                string defaultPath = Path.Combine(ebWebViewPath, "Default");
                if (Directory.Exists(defaultPath))
                {
                    foreach (var item in Directory.GetFileSystemEntries(defaultPath))
                    {
                        string name = Path.GetFileName(item);

                        if (name.Equals("Network", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        DeleteWithRetry(item);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка очистки EBWebView: {ex.Message}");
            }
        }

        private void DeleteWithRetry(string path, int maxRetries = 5, int delayMs = 300)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        // Снимаем атрибуты "только чтение" со всех файлов в папке
                        var dirInfo = new DirectoryInfo(path);
                        foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                        {
                            try { file.IsReadOnly = false; } catch { }
                        }
                        Directory.Delete(path, true);
                        Debug.WriteLine($"  Удалена папка: {Path.GetFileName(path)}");
                        return;
                    }
                    else if (File.Exists(path))
                    {
                        File.SetAttributes(path, FileAttributes.Normal);
                        File.Delete(path);
                        Debug.WriteLine($"  Удалён файл: {Path.GetFileName(path)}");
                        return;
                    }
                    return;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"  Попытка {i + 1} не удалась: {ex.Message}");
                    if (i < maxRetries - 1)
                    {
                        Thread.Sleep(delayMs);
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }
                }
            }
        }
    }
}