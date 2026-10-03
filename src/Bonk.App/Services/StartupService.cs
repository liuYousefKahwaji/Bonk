using System.Diagnostics;
using Microsoft.Win32;

namespace Bonk.App.Services;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("Bonk") is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (!enabled) { key.DeleteValue("Bonk", throwOnMissingValue: false); return; }
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot find Bonk's executable.");
        var launch = Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? $"\"{path}\" \"{typeof(StartupService).Assembly.Location}\" --tray" : $"\"{path}\" --tray";
        key.SetValue("Bonk", launch);
    }

    public static void RestartElevated(string configPath)
    {
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot find Bonk's executable.");
        var info = new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" };
        var prefix = Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? $"\"{typeof(StartupService).Assembly.Location}\" " : "";
        info.Arguments = $"{prefix}--restart --config \"{configPath}\"";
        Process.Start(info)?.Dispose();
    }
}
