namespace GroupTasker.Domain.ValueObjects;

/// <summary>
/// Outcome of a "pin group to taskbar" operation. Structured rather than a free-form
/// English string so the UI is responsible for formatting.
/// </summary>
public enum PinOutcome
{
    /// <summary>The shortcut was successfully pinned to the taskbar.</summary>
    Pinned,

    /// <summary>The shortcut was created on disk but taskbar pinning is not available on this OS.</summary>
    ShortcutCreatedManualPinRequired,

    /// <summary>The operation failed; <see cref="PinResult.Error"/> carries the reason.</summary>
    Failed
}

/// <summary>Result of <c>GroupService.PinGroupToTaskbarAsync</c>.</summary>
public sealed record PinResult(PinOutcome Outcome, string LauncherPath, string? Error = null);
