using System.Diagnostics;
using System.IO;

namespace PerformanceMonitor.Wpf.Services;

/// <summary>
/// 开机自启：计划任务（schtasks），与 Electron 版行为一致。
/// exe 复制到 %AppData%\SystemMonitor 后注册 onlogon 最高权限任务。
/// </summary>
public static class AutoStartService
{
    private const string TaskName = "SystemMonitor";

    private static string SchTasks =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");

    private static string AppDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), TaskName);

    private static string ExeSource => Environment.ProcessPath;

    private static string ExeTarget => Path.Combine(AppDir, Path.GetFileName(ExeSource) ?? "PerformanceMonitor.exe");

    public static bool Get()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = SchTasks,
                Arguments = $"/query /tn {TaskName} /fo list",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool Set(bool enabled)
    {
        try
        {
            if (enabled)
            {
                Directory.CreateDirectory(AppDir);
                File.Copy(ExeSource, ExeTarget, overwrite: true);

                var psi = new ProcessStartInfo
                {
                    FileName = SchTasks,
                    Arguments = $"/create /tn {TaskName} /tr \"{ExeTarget}\" /sc onlogon /it /rl highest /f",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                p.WaitForExit(10000);
                if (p.ExitCode != 0)
                {
                    TryCleanup();
                    return false;
                }
                return true;
            }
            else
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = SchTasks,
                    Arguments = $"/delete /tn {TaskName} /f",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                p.WaitForExit(10000);
                return p.ExitCode == 0;
            }
        }
        catch
        {
            TryCleanup();
            return false;
        }
    }

    private static void TryCleanup()
    {
        try { Directory.Delete(AppDir, recursive: true); } catch { }
    }
}
