using GroupTasker.Domain.Entities;

namespace GroupTasker.Domain.Interfaces;

/// <summary>
/// Discovers Windows taskbar items so the user can add them to a group even
/// when there's no desktop shortcut (common for auto-updating apps like
/// Claude Desktop, Codex, etc.). Each item carries enough info to launch it
/// without depending on a stable .exe path.
/// </summary>
public sealed class DiscoveredApp
{
    /// <summary>Human-readable name shown in the picker.</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// AppUserModelId if known (preferred — survives updates). Null for plain
    /// Win32 apps where we only have the process name.
    /// </summary>
    public string? Aumi { get; init; }

    /// <summary>Process name (e.g. "claude"). Used as a fallback for Win32 apps.</summary>
    public string? ProcessName { get; init; }

    /// <summary>Resolved .exe path at discovery time, if available.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Window handle of the running instance, if any.</summary>
    public IntPtr WindowHandle { get; init; }

    /// <summary>Where this discovery came from (running window, taskbar pin, etc.).</summary>
    public required DiscoveredAppSource Source { get; init; }
}

public enum DiscoveredAppSource
{
    RunningWindow,
    PinnedTaskbar,
    StoreApp
}

/// <summary>
/// Enumerates taskbar-pinned apps and currently-running windows. Used by the
/// "Add from running apps" UI in the configurator.
/// </summary>
public interface ITaskbarEnumerator
{
    /// <summary>Get a snapshot of discoverable apps (running windows + taskbar pins).</summary>
    IReadOnlyList<DiscoveredApp> Enumerate();
}
