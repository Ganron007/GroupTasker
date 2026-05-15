using System.Runtime.InteropServices;
using System.Text;

namespace GroupTasker.Infrastructure.Shell;

/// <summary>
/// COM interop wrappers for Windows Shell Link (<c>IShellLinkW</c>), Property Store, and PROPVARIANT.
/// Ported from the original TaskbarGroups <c>ShellLink.cs</c>, modernised with explicit marshalling.
/// </summary>
public static class ShellLinkInterop
{
    /// <summary>Create a .lnk shortcut file with AppUserModelID for taskbar non-stacking behaviour.</summary>
    public static void CreateShortcut(
        string targetPath,
        string appUserModelId,
        string description,
        string workingDirectory,
        string iconLocation,
        string savePath,
        string? arguments = null)
    {
        var shortcut = (IShellLinkW)new CShellLink();
        shortcut.SetPath(targetPath);
        shortcut.SetDescription(description);
        shortcut.SetWorkingDirectory(workingDirectory);
        shortcut.SetIconLocation(iconLocation, 0);

        if (arguments is not null)
            shortcut.SetArguments(arguments);

        var propertyStore = (IPropertyStore)shortcut;
        using var propVariant = new PropVariant(appUserModelId);
        var key = PropertyKey.AppUserModelId;
        propertyStore.SetValue(ref key, ref propVariant.Value);
        propertyStore.Commit();

        var persistFile = (IPersistFile)shortcut;
        persistFile.Save(savePath, true);
    }

    /// <summary>
    /// Read a .lnk's target / arguments / working directory via <c>IShellLinkW</c>.
    /// Replaces the old <c>dynamic</c>/WScript.Shell automation path.
    /// </summary>
    public static (string TargetPath, string Arguments, string WorkingDirectory) ReadShortcut(string lnkPath)
    {
        var shortcut = (IShellLinkW)new CShellLink();
        var persist = (IPersistFile)shortcut;
        persist.Load(lnkPath, 0);

        var pathBuf = new StringBuilder(MAX_PATH);
        shortcut.GetPath(pathBuf, pathBuf.Capacity, IntPtr.Zero, 0);

        var argsBuf = new StringBuilder(MAX_PATH);
        shortcut.GetArguments(argsBuf, argsBuf.Capacity);

        var dirBuf = new StringBuilder(MAX_PATH);
        shortcut.GetWorkingDirectory(dirBuf, dirBuf.Capacity);

        return (pathBuf.ToString(), argsBuf.ToString(), dirBuf.ToString());
    }

    private const int MAX_PATH = 260;

    #region COM Interfaces

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotKey(out short wHotKey);
        void SetHotKey(short wHotKey);
        void GetShowCmd(out uint iShowCmd);
        void SetShowCmd(uint iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int iIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, long dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint propertyCount);
        void GetAt(uint propertyIndex, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariantStruct pv);
        void SetValue(ref PropertyKey key, ref PropVariantStruct pv);
        void Commit();
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046"), ClassInterface(ClassInterfaceType.None)]
    private class CShellLink { }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariantStruct
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr unionmember;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;

        public static readonly PropertyKey AppUserModelId =
            new() { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };
    }

    private sealed class PropVariant : IDisposable
    {
        private PropVariantStruct _variant;
        public ref PropVariantStruct Value => ref _variant;

        public PropVariant(string value)
        {
            _variant.vt = (ushort)VarEnum.VT_LPWSTR;
            _variant.unionmember = Marshal.StringToCoTaskMemUni(value);
        }

        public void Dispose()
        {
            if (_variant.unionmember != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(_variant.unionmember);
                _variant.unionmember = IntPtr.Zero;
            }
        }
    }

    #endregion
}
