using System;
using System.Runtime.InteropServices;

namespace GroupTasker.UI.ViewModels;

/// <summary>
/// P/Invoke wrappers for shell-level operations the launcher view-model needs.
/// Kept in the UI layer because they're tied to the user's interactive flow
/// (right-click menu actions), not to the application's domain logic.
/// </summary>
internal static class ShellInterop
{
    /// <summary>Show the Windows file Properties dialog for the given path.</summary>
    public static void ShowObjectProperties(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        // shopObjectType = 1 → SHOP_FILEPATH (a path on the local filesystem)
        SHObjectProperties(IntPtr.Zero, 1, path, null);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern bool SHObjectProperties(IntPtr hwnd, uint shopObjectType, string pszObjectName, string? pszPropertyPage);
}
