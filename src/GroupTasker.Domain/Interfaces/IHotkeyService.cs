using GroupTasker.Domain.Entities;

namespace GroupTasker.Domain.Interfaces;

/// <summary>
/// Registers a single system-wide hotkey and raises an event when the user presses it.
/// Owns its own message-only Win32 window on a background thread so the rest of the app
/// is unaffected.
/// </summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>
    /// Register <paramref name="binding"/>. If a previous binding is still active, it is
    /// unregistered first. Returns <c>true</c> on success; <c>false</c> if the OS refused
    /// (e.g. another app already owns the same combo).
    /// </summary>
    bool TryRegister(HotkeyBinding binding);

    /// <summary>Unregister the currently active binding, if any. No-op if nothing is registered.</summary>
    void Unregister();

    /// <summary>The currently registered binding, or <c>null</c> if none.</summary>
    HotkeyBinding? CurrentBinding { get; }

    /// <summary>Raised on the message-pump thread when the OS delivers <c>WM_HOTKEY</c>. Subscribers
    /// should marshal to the UI thread if they touch UI.</summary>
    event Action? HotkeyPressed;
}
