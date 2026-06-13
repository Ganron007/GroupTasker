namespace GroupTasker.Domain.Interfaces;

/// <summary>
/// Resolves the current launch path for a "live application" shortcut. Used when
/// a shortcut is stored by AUMI or process name and the .exe has moved (e.g.
/// an auto-update replaced the binaries in a new versioned folder).
/// </summary>
public interface ILiveAppResolver
{
    /// <summary>
    /// Given a stored AUMI or process name, return the current .exe path
    /// (or null if the app cannot be located). Resolution order:
    /// 1. Currently-running process with matching name
    /// 2. Windows App Paths registry (HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths)
    /// 3. PATH search
    /// </summary>
    string? Resolve(string aumiOrProcessName);
}
