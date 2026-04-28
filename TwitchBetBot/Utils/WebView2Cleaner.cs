using System;
using System.IO;
using System.Diagnostics;
using System.Threading;

namespace TwitchBetBot.Utils
{
    public static class WebView2Cleaner
    {
        public static void CleanEBWebView()
        {
            try
            {
                string ebWebViewPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "TwitchBetBot.exe.WebView2",
                    "EBWebView");

                if (!Directory.Exists(ebWebViewPath))
                {
                    Debug.WriteLine($"Папка не найдена: {ebWebViewPath}");
                    return;
                }

                Debug.WriteLine($"Очистка: {ebWebViewPath}");

                // Удаляем всё, кроме Default и Local State
                foreach (var item in Directory.GetFileSystemEntries(ebWebViewPath))
                {
                    string name = Path.GetFileName(item);

                    if (name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Local State", StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine($"Сохраняем: {name}");
                        continue;
                    }

                    DeleteWithRetry(item);
                }

                // Очищаем Default, но сохраняем Network
                string defaultPath = Path.Combine(ebWebViewPath, "Default");
                if (Directory.Exists(defaultPath))
                {
                    foreach (var item in Directory.GetFileSystemEntries(defaultPath))
                    {
                        string name = Path.GetFileName(item);

                        if (name.Equals("Network", StringComparison.OrdinalIgnoreCase))
                        {
                            Debug.WriteLine($"Сохраняем: {name}");
                            continue;
                        }

                        DeleteWithRetry(item);
                    }
                }

                Debug.WriteLine("Очистка завершена");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка: {ex.Message}");
            }
        }

        private static void DeleteWithRetry(string path, int maxRetries = 3, int delayMs = 500)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                    }
                    else if (File.Exists(path))
                    {
                        File.SetAttributes(path, FileAttributes.Normal);
                        File.Delete(path);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Попытка {i + 1}: {ex.Message}");
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