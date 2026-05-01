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

            string batPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "clean.bat");

            if (File.Exists(batPath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = batPath,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                Process.Start(psi);
            }
        }
    }
}