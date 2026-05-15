using GroupTasker.Domain.Entities;

namespace GroupTasker.Domain.Interfaces;

/// <summary>
/// Contracts for launching and resolving shortcuts. Infrastructure concerns stay behind this interface.
/// </summary>
public interface IShortcutService
{
    /// <summary>Resolve a source path to its actual target and determine the shortcut type.</summary>
    Shortcut Resolve(string sourcePath);

    /// <summary>Launch a shortcut (process start, shell execute, or store app activation).</summary>
    void Launch(Shortcut shortcut);

    /// <summary>Create a temporary .lnk file for drag-and-drop operations.</summary>
    string CreateTempLink(Shortcut shortcut);

    /// <summary>Pin a group's launcher shortcut to the Windows taskbar. Returns true if pinning succeeded.</summary>
    bool PinToTaskbar(Group group, string launcherPath);

    /// <summary>
    /// Create a persistent .lnk shortcut that launches GroupTasker.exe with the
    /// group name as a CLI argument, causing the LauncherWindow to open for that group.
    /// The .lnk is placed in a 'shortcut' folder under the app's root directory.
    /// Returns the full path to the created .lnk file.
    /// </summary>
    string CreateGroupLauncherLink(Group group, string iconPath);
}
