using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace DarumaPPAP.PlateauWorldFlow
{
    internal sealed class ToolProbe
    {
        internal static Task<string> Start() => Task.Run(Check);

        private static string Check()
        {
            // UPM invokes Git separately. This probe checks availability without modifying Git settings.
            if (!CanRun("--version")) return "GIT_UNAVAILABLE";

            return CanRun("lfs version") ? null : "GIT_LFS_UNAVAILABLE";
        }

        private static bool CanRun(string arguments)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo("git", arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
                // A failure to launch cannot establish the tool prerequisite.
                if (process == null) return false;

                if (process.WaitForExit(3000))
                {
                    return process.ExitCode == 0;
                }

                process.Kill();
                return false;
            }
            catch (Exception error) when (error is Win32Exception || error is InvalidOperationException || error is PlatformNotSupportedException)
            {
                return false;
            }
        }
    }
}
