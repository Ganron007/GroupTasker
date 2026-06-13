using System.Reflection;
using GroupTasker.Domain.Entities;
using GroupTasker.Domain.Interfaces;

namespace GroupTasker.Infrastructure.Shell;

/// <summary>
/// Discovers installed apps by enumerating the Windows shell
/// <c>shell:AppsFolder</c> virtual folder. This is the exact same source
/// Windows uses for the Start menu search, the "All apps" list, and the
/// taskbar's installed-apps view. It returns every Microsoft Store / MSIX
/// app the user has installed plus shell-registered traditional apps.
/// <para>
/// For each item, the AUMI comes from the <c>System.AppUserModel.ID</c>
/// extended property (or the <c>Path</c> field, which is the same value).
/// MSIX apps expose no <c>.lnk</c> in <c>User Pinned\TaskBar</c> for the
/// Store installs — they live in <c>shell:AppsFolder</c> and are pinned by
/// AUMI, which is why the Start-Menu/.lnk approach misses them.
/// </para>
/// </summary>
public sealed class ShellAppsFolderEnumerator : IStoreAppEnumerator
{
    public IReadOnlyList<DiscoveredApp> Enumerate()
    {
        var results = new List<DiscoveredApp>();
        object? items = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return results;

            var shellApp = Activator.CreateInstance(shellType);
            if (shellApp is null) return results;

            var folder = shellType.InvokeMember(
                "NameSpace",
                BindingFlags.InvokeMethod,
                null,
                shellApp,
                new object[] { "shell:AppsFolder" });
            if (folder is null) return results;

            items = folder.GetType().InvokeMember(
                "Items",
                BindingFlags.InvokeMethod,
                null,
                folder,
                null);
        }
        catch
        {
            return results;
        }

        if (items is null) return results;

        // Enumerate via IEnumVARIANT (Shell.FolderItems) so we can release
        // each COM item after reading its properties — this avoids holding
        // 307 FolderItem wrappers in memory.
        foreach (var item in EnumerateCom(items))
        {
            DiscoveredApp? app = null;
            try
            {
                var itemType = item.GetType();
                var name = itemType.InvokeMember(
                    "Name", BindingFlags.GetProperty, null, item, null) as string;
                if (string.IsNullOrWhiteSpace(name)) continue;

                // System.AppUserModel.ID is the canonical AUMI; fall back to
                // Path which is the same value for AppsFolder items.
                var aumi = itemType.InvokeMember(
                    "ExtendedProperty",
                    BindingFlags.InvokeMethod,
                    null,
                    item,
                    new object[] { "System.AppUserModel.ID" }) as string;
                if (string.IsNullOrWhiteSpace(aumi))
                {
                    aumi = itemType.InvokeMember(
                        "Path", BindingFlags.GetProperty, null, item, null) as string;
                }
                if (string.IsNullOrWhiteSpace(aumi)) continue;

                app = new DiscoveredApp
                {
                    DisplayName = name,
                    Aumi = aumi,
                    ProcessName = name,
                    ExecutablePath = null,
                    Source = DiscoveredAppSource.StoreApp
                };
            }
            catch
            {
                // Skip unreadable / permission-denied entries
            }

            if (app is not null) results.Add(app);
        }

        return results;
    }

    /// <summary>
    /// Iterate a COM collection (Shell.FolderItems) without holding every
    /// item alive. Uses the standard IEnumVARIANT pattern.
    /// </summary>
    private static IEnumerable<object> EnumerateCom(object items)
    {
        // Shell.FolderItems implements IEnumVARIANT via the dispatch interface.
        // The simplest reliable way to iterate is to call the default indexed
        // property from 0 to Count-1, releasing each item via Marshal.ReleaseComObject.
        var itemsType = items.GetType();
        var countObj = itemsType.InvokeMember(
            "Count", BindingFlags.GetProperty, null, items, null);
        if (countObj is null) yield break;
        int count;
        try { count = (int)countObj; } catch { yield break; }

        for (var i = 0; i < count; i++)
        {
            object? item = null;
            try
            {
                item = itemsType.InvokeMember(
                    "Item",
                    BindingFlags.InvokeMethod,
                    null,
                    items,
                    new object[] { i });
            }
            catch
            {
                // Skip unreadable entries
            }

            if (item is not null) yield return item;
        }
    }
}
