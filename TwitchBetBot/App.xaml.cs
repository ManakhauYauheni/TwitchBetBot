using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace TwitchBetBot
{
    public partial class App : Application
    {
        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);

            // Путь к папке EBWebView
            string ebWebViewPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "TwitchBetBot.exe.WebView2",
                "EBWebView");

            // Если папки нет - выходим
            if (!Directory.Exists(ebWebViewPath))
                return;

            // PowerShell скрипт для очистки
            string script = $@"
Start-Sleep -Seconds 2
$path = '{ebWebViewPath}'
if (Test-Path $path) {{
    # Удаляем всё, кроме Default и Local State
    Get-ChildItem $path | Where-Object {{ $_.Name -ne 'Default' -and $_.Name -ne 'Local State' }} | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    
    # Очищаем папку Default, но сохраняем Network
    $defaultPath = Join-Path $path 'Default'
    if (Test-Path $defaultPath) {{
        Get-ChildItem $defaultPath | Where-Object {{ $_.Name -ne 'Network' }} | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }}
}}
";

            // Сохраняем скрипт во временный файл
            string tempScript = Path.Combine(Path.GetTempPath(), $"webview2_clean_{DateTime.Now.Ticks}.ps1");

            try
            {
                File.WriteAllText(tempScript, script);

                // Запускаем PowerShell
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-WindowStyle Hidden -ExecutionPolicy Bypass -File \"{tempScript}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                Process.Start(psi);

                // Удаляем скрипт через 10 секунд
                var deleteTimer = new System.Timers.Timer(10000);
                deleteTimer.Elapsed += (s, args) => {
                    try { File.Delete(tempScript); } catch { }
                    deleteTimer.Dispose();
                };
                deleteTimer.AutoReset = false;
                deleteTimer.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка при очистке: {ex.Message}");
            }
        }
    }
}