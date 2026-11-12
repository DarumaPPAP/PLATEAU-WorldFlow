using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DarumaPPAP.PlateauWorldFlow
{
    internal sealed class ToolProbe
    {
        internal static Task<(string Failure, Dictionary<string, object>[] Tools)> Start() => Task.Run(Check);

        private static (string Failure, Dictionary<string, object>[] Tools) Check()
        {
            // UPMのGit設定には触れず、実際に起動したコマンドの応答だけを記録する。
            var git = Probe("--version", "git version ", false);
            var lfs = git.Status == "READY" ? Probe("lfs version", "git-lfs/", true) : ("BLOCKED", null);
            string failure = git.Status != "READY" ? "GIT_UNAVAILABLE" : lfs.Item1 != "READY" ? "GIT_LFS_UNAVAILABLE" : null;
            return (failure, new[] { Entry("git", git.Status, git.Version, true), Entry("git-lfs", lfs.Item1, lfs.Item2, git.Status == "READY") });
        }

        internal static Dictionary<string, object>[] UnknownTools()
        {
            return new[] { Entry("git", "UNKNOWN", null, false), Entry("git-lfs", "UNKNOWN", null, false) };
        }

        private static Dictionary<string, object> Entry(string id, string status, string version, bool attempted)
        {
            return Json.Object(("toolId", id), ("resolvedVersion", version), ("status", status), ("evidence", attempted ? new[] { "TOOL_PROBE" } : Array.Empty<string>()));
        }

        private static (string Status, string Version) Probe(string arguments, string prefix, bool lfs)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo("git", arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
                // 起動結果が得られなければ利用可否を判断できない。
                if (process == null) return ("UNKNOWN", null);

                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(3000))
                {
                    process.Kill();
                    return ("UNKNOWN", null);
                }

                if (!Task.WaitAll(new Task[] { output, error }, 1000))
                {
                    return ("UNKNOWN", null);
                }
                if (process.ExitCode != 0)
                {
                    bool missingLfs = lfs && error.Result.Contains("'lfs' is not a git command", StringComparison.Ordinal);
                    return (missingLfs ? "MISSING" : "UNKNOWN", null);
                }

                var match = Regex.Match(output.Result, @"\A" + Regex.Escape(prefix) + @"([0-9][A-Za-z0-9._+-]{0,127})(?=\s|\z)", RegexOptions.CultureInvariant);
                return ("READY", match.Success ? match.Groups[1].Value : null);
            }
            catch (Win32Exception error) when (!lfs && (error.NativeErrorCode == 2 || error.NativeErrorCode == 3))
            {
                return ("MISSING", null);
            }
            catch (Exception error) when (error is Win32Exception || error is InvalidOperationException || error is PlatformNotSupportedException || error is AggregateException)
            {
                return ("UNKNOWN", null);
            }
        }
    }
}
