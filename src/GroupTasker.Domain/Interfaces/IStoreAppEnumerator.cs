using GroupTasker.Domain.Entities;

namespace GroupTasker.Domain.Interfaces;

/// <summary>
/// Enumerates Microsoft Store / MSIX apps installed under
/// <c>C:\Program Files\WindowsApps\</c>. These are the official install locations
/// of apps like Claude Desktop, Codex, etc., and the path changes with every
/// app update (e.g., <c>Claude_1.12603.1.0_x64__pzs8sxrjxfjjc</c>).
/// <para>
/// Each store app carries an <see cref="DiscoveredApp.Aumi"/> of the form
/// <c>PackageFamilyName!ApplicationId</c> (e.g. <c>Claude_pzs8sxrjxfjjc!Claude</c>).
/// This AUMI is the stable identifier that survives version bumps — Windows
/// resolves it to the current version via the AppUserModelID registration.
/// </para>
/// </summary>
public interface IStoreAppEnumerator
{
    /// <summary>
    /// Enumerate every MSIX/Store app under WindowsApps. Filters out system
    /// packages and Microsoft.* infrastructure. Returns one
    /// <see cref="DiscoveredApp"/> per app with the AUMI and main .exe path.
    /// </summary>
    IReadOnlyList<DiscoveredApp> Enumerate();
}
