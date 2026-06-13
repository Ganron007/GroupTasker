using System.Runtime.InteropServices;
using GroupTasker.Domain.Interfaces;

namespace GroupTasker.Infrastructure.Shell;

/// <summary>
/// Launches Microsoft Store / MSIX apps by their AppUserModelId via the
/// Windows <c>IApplicationActivationManager</c> COM interface.
/// <para>
/// This is a pure IUnknown-based COM interface (not IDispatch), so it requires
/// proper COM interop — <c>Type.InvokeMember</c> won't work because
/// IDispatch-only. The previous attempt failed with
/// "COM target does not implement IDispatch" and silently returned false.
/// </para>
/// <para>
/// This activator uses the correct IID
/// (<c>2e941141-7f97-4756-ba1d-9decde894a3d</c>) verified against the
/// Windows SDK header <c>shobjidl_core.h</c>. A previous build had a typo
/// in the IID (<c>894a3b3</c> vs <c>894a3d</c>) which caused the cast to
/// fail at runtime.
/// </para>
/// </summary>
public sealed class WindowsAppActivator : IAppActivator
{
    /// <summary>
    /// The IID of <c>IApplicationActivationManager</c>. Verified against
    /// <c>shobjidl_core.h</c> in the Windows 10 SDK.
    /// </summary>
    private static readonly Guid IID_IApplicationActivationManager =
        new("2e941141-7f97-4756-ba1d-9decde894a3d");

    /// <summary>CLSID of the CApplicationActivationManager class.</summary>
    private static readonly Guid CLSID_ApplicationActivationManager =
        new("45BA127D-10A8-46EA-8AB7-56EA9078943C");

    public bool ActivateByAumi(string appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId)) return false;
        if (!appUserModelId.Contains('!')) return false;

        try
        {
            var type = System.Type.GetTypeFromCLSID(CLSID_ApplicationActivationManager);
            if (type is null) return false;

            var instance = System.Activator.CreateInstance(type);
            if (instance is null) return false;

            // Direct cast to the v-table COM interface. The runtime creates
            // a __ComObject which satisfies the IUnknown-derived interface.
            var manager = (IApplicationActivationManager)instance;

            uint pid = 0;
            manager.ActivateApplication(appUserModelId, null, 0, out pid);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string? TryGetAumiFromLink(string lnkPath)
    {
        if (string.IsNullOrWhiteSpace(lnkPath) || !File.Exists(lnkPath)) return null;

        // Use Shell.Application automation to read the System.AppUserModel.ID
        // property of a taskbar .lnk. This is the same automation surface that
        // Explorer uses and is reliable across Win10/11.
        try
        {
            var shellType = System.Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return null;
            var shellApp = Activator.CreateInstance(shellType);
            if (shellApp is null) return null;

            var folder = shellType.InvokeMember(
                "NameSpace",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shellApp,
                new object[] { Path.GetDirectoryName(lnkPath) ?? "" });
            if (folder is null) return null;

            var items = folder.GetType().InvokeMember(
                "Items",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                folder,
                null);
            if (items is null) return null;

            var item = items.GetType().InvokeMember(
                "Item",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                items,
                new object[] { Path.GetFileName(lnkPath) });
            if (item is null) return null;

            var extendedString = item.GetType().InvokeMember(
                "ExtendedProperty",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                item,
                new object[] { "System.AppUserModel.ID" }) as string;

            return string.IsNullOrWhiteSpace(extendedString) ? null : extendedString;
        }
        catch
        {
            return null;
        }
    }

    [ComImport]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        void ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            int options,
            out uint processId);
    }
}
