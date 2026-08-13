using GroupTasker.Domain.Interfaces;

namespace GroupTasker.Domain.Interfaces;

/// <summary>
/// Discovers installed games from major launchers (Steam, Epic, GOG) so the
/// "Add from running apps" picker can list games even when they are not
/// currently running and not pinned to the taskbar.
/// </summary>
public interface IGameLibraryEnumerator
{
    /// <summary>Get a snapshot of installed games. Never throws.</summary>
    IReadOnlyList<DiscoveredApp> Enumerate();
}
