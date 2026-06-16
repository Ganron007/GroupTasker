using System.Runtime.InteropServices;
using GroupTasker.Domain.Interfaces;
using GroupTasker.Domain.Logging;

namespace GroupTasker.Infrastructure.Shell;

/// <summary>
/// Win32 <c>Shell_NotifyIconW</c> implementation. Owns a hidden message-only
/// window on a dedicated background thread. The icon's callback messages are
/// intercepted in the message loop (between <c>GetMessage</c> and
/// <c>DispatchMessage</c>) so the rest of the app is unaffected.
/// </summary>
public sealed class TrayIconService : ITrayIconService
{
    private const uint WM_APP = 0x8000;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_NONOTIFY = 0x0080;

    private readonly ILogger _logger;
    private readonly object _gate = new();
    private IntPtr _hwnd;
    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private string _tooltip = "GroupTasker";
    private IntPtr _hIcon;
    private List<TrayMenuItem> _menuItems = [];
    private bool _added;
    private bool _disposed;

    public TrayIconService(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    public event Action? IconClicked;
    public event Action<string>? MenuAction;

    public void Show(string tooltipText)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TrayIconService));
        _tooltip = tooltipText ?? "GroupTasker";

        lock (_gate)
        {
            EnsureWindow();
            if (_added)
            {
                ModifyIcon();
            }
            else
            {
                AddIcon();
            }
        }
    }

    public void Hide()
    {
        lock (_gate)
        {
            if (_added && _hwnd != IntPtr.Zero)
            {
                var nid = BuildNotifyData(NIM_DELETE);
                Shell_NotifyIcon(NIM_DELETE, ref nid);
                _added = false;
            }
        }
    }

    public void SetMenu(IReadOnlyList<TrayMenuItem> items)
    {
        lock (_gate) _menuItems = [.. items];
    }

    public void SetTooltip(string tooltipText)
    {
        _tooltip = tooltipText ?? "GroupTasker";
        lock (_gate)
        {
            if (_added) ModifyIcon();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Hide();
        try { _cts?.Cancel(); } catch { /* ignore */ }
        _thread?.Join(TimeSpan.FromSeconds(1));
        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
        _cts?.Dispose();
    }

    // --- Implementation ---

    private void EnsureWindow()
    {
        if (_hwnd != IntPtr.Zero && _thread?.IsAlive == true) return;

        _cts = new CancellationTokenSource();
        var ready = new ManualResetEventSlim(false);
        _thread = new Thread(() => MessagePumpThread(ready, _cts!.Token))
        {
            IsBackground = true,
            Name = "GroupTasker.TrayIconMessagePump"
        };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(2));
    }

    private void MessagePumpThread(ManualResetEventSlim ready, CancellationToken ct)
    {
        _hwnd = CreateWindowEx(
            0, "Static", "GroupTaskerTray", 0,
            0, 0, 0, 0,
            new IntPtr(-3), // HWND_MESSAGE
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            _logger.Error("Failed to create message-only window for tray icon");
            ready.Set();
            return;
        }

        // Load icon from the running exe (first icon resource).
        var exePath = Environment.ProcessPath;
        if (exePath is not null)
            _hIcon = ExtractIcon(IntPtr.Zero, exePath, 0);
        if (_hIcon == IntPtr.Zero || _hIcon == new IntPtr(1))
            _hIcon = LoadIcon(IntPtr.Zero, 32512); // IDI_APPLICATION

        ready.Set();

        while (!ct.IsCancellationRequested)
        {
            if (!PeekMessage(out var msg, IntPtr.Zero, 0, 0, 0))
            {
                Thread.Sleep(15);
                continue;
            }

            if (GetMessage(out msg, IntPtr.Zero, 0, 0) <= 0) break;

            // Tray icon callback: WM_APP + 0
            if (msg.message == WM_APP + 0 && msg.hwnd == _hwnd)
            {
                var mouseMsg = (uint)(msg.lParam.ToInt64() & 0xFFFF);
                if (mouseMsg == WM_LBUTTONUP)
                {
                    try { IconClicked?.Invoke(); }
                    catch (Exception ex) { _logger.Error(ex, "TrayIcon IconClicked handler threw"); }
                }
                else if (mouseMsg == WM_RBUTTONUP)
                {
                    ShowContextMenu();
                }
                continue;
            }

            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    private void ShowContextMenu()
    {
        GetCursorPos(out var pt);
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;

        List<TrayMenuItem> snapshot;
        lock (_gate) snapshot = [.. _menuItems];

        uint id = 1;
        var idMap = new Dictionary<uint, string>();
        foreach (var item in snapshot)
        {
            if (item.IsSeparator)
            {
                AppendMenu(menu, 0x0800 /*MF_SEPARATOR*/, 0, null); // lpszText null for separator
            }
            else
            {
                uint flags = 0x0000; // MF_STRING
                if (item.IsChecked) flags |= 0x0008; // MF_CHECKED
                AppendMenu(menu, flags, id, item.Label);
                idMap[id] = item.ActionKey;
                id++;
            }
        }

        // Required for the menu to dismiss correctly when clicking outside.
        SetForegroundWindow(_hwnd);

        var selected = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY,
                                       pt.x, pt.y, 0, _hwnd, IntPtr.Zero);

        // Acknowledge the menu dismiss.
        PostMessage(_hwnd, 0x0000 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);

        DestroyMenu(menu);

        if (selected > 0 && idMap.TryGetValue(selected, out var actionKey))
        {
            try { MenuAction?.Invoke(actionKey); }
            catch (Exception ex) { _logger.Error(ex, "TrayIcon MenuAction handler threw for {ActionKey}", actionKey); }
        }
    }

    // --- Shell_NotifyIcon plumbing ---

    private void AddIcon()
    {
        var nid = BuildNotifyData(NIM_ADD);
        _added = Shell_NotifyIcon(NIM_ADD, ref nid);
        if (_added)
            _logger.Information("Tray icon added");
        else
            _logger.Warning("Failed to add tray icon");
    }

    private void ModifyIcon()
    {
        var nid = BuildNotifyData(NIM_MODIFY);
        Shell_NotifyIcon(NIM_MODIFY, ref nid);
    }

    private NOTIFYICONDATA BuildNotifyData(uint message)
    {
        var tip = _tooltip ?? "GroupTasker";
        if (tip.Length > 127) tip = tip[..127]; // szTip is SizeConst=128, leave room for null terminator

        return new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_APP + 0,
            hIcon = _hIcon,
            szTip = tip,
        };
    }

    // --- Win32 structs + P/Invoke ---

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, uint lpIconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(IntPtr hMenu, uint uFlags,
        int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);
}
