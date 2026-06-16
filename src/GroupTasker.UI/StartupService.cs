using System;
using Microsoft.Win32;

namespace GroupTasker.UI;

/// <summary>
/// Manages the per-user "start with Windows" registry entry under HKCU.
/// When enabled, Windows launches the app with the <c>--tray</c> argument on login,
/// so the global hotkey and tray icon work in the background.
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "GroupTasker";

    /// <summary>Enable or disable auto-start. Returns true on success.</summary>
    public static bool SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return false;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (exePath is null) return false;
                key.SetValue(AppName, $"\"{exePath}\" --tray", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Check whether auto-start is currently enabled.</summary>
    public static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(AppName) is not null;
        }
        catch
        {
            return false;
        }
    }
}
