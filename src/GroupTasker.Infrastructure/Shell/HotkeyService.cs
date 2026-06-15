using System.Runtime.InteropServices;
using GroupTasker.Domain.Entities;
using GroupTasker.Domain.Interfaces;
using GroupTasker.Domain.Logging;

namespace GroupTasker.Infrastructure.Shell;

/// <summary>
/// Win32 <c>RegisterHotKey</c> implementation. Owns a dedicated background thread that hosts
/// a hidden message-only window and pumps Win32 messages so <c>WM_HOTKEY</c> is delivered
/// without coupling to the Avalonia UI thread.
/// </summary>
public sealed class HotkeyService : IHotkeyService
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0x9001;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly ILogger _logger;
    private readonly object _gate = new();
    private IntPtr _hwnd;
    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private HotkeyBinding? _current;
    private bool _disposed;

    public HotkeyService(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    public event Action? HotkeyPressed;

    public HotkeyBinding? CurrentBinding { get { lock (_gate) return _current; } }

    public bool TryRegister(HotkeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (_disposed) throw new ObjectDisposedException(nameof(HotkeyService));

        lock (_gate)
        {
            EnsureMessageWindow();
            UnregisterNoLock();
            var mods = (uint)binding.Modifiers | MOD_NOREPEAT;
            var ok = RegisterHotKey(_hwnd, HotkeyId, mods, (uint)binding.Key);
            if (ok)
            {
                _current = binding;
                _logger.Information("Registered global hotkey {Hotkey}", binding);
            }
            else
            {
                _logger.Warning("Failed to register global hotkey {Hotkey} (in use by another app?)", binding);
            }
            return ok;
        }
    }

    public void Unregister()
    {
        lock (_gate) UnregisterNoLock();
    }

    private void UnregisterNoLock()
    {
        if (_hwnd != IntPtr.Zero && _current is not null)
        {
            UnregisterHotKey(_hwnd, HotkeyId);
            _logger.Information("Unregistered global hotkey {Hotkey}", _current);
        }
        _current = null;
    }

    private void EnsureMessageWindow()
    {
        if (_hwnd != IntPtr.Zero && _thread?.IsAlive == true) return;

        _cts = new CancellationTokenSource();
        var ready = new ManualResetEventSlim(false);
        _thread = new Thread(() => MessagePumpThread(ready, _cts.Token))
        {
            IsBackground = true,
            Name = "GroupTasker.HotkeyMessagePump"
        };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(2));
    }

    private void MessagePumpThread(ManualResetEventSlim ready, CancellationToken ct)
    {
        _hwnd = CreateWindowEx(
            0, "Static", "GroupTaskerHotkey", 0,
            0, 0, 0, 0,
            HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            _logger.Error("Failed to create message-only window for hotkey service");
            ready.Set();
            return;
        }

        ready.Set();

        while (!ct.IsCancellationRequested)
        {
            // PeekMessage with PM_NOREMOVE so we can also notice cancellation promptly.
            if (!PeekMessage(out var msg, IntPtr.Zero, 0, 0, PM_NOREMOVE))
            {
                Thread.Sleep(15);
                continue;
            }

            if (GetMessage(out msg, IntPtr.Zero, 0, 0) <= 0) break;

            if (msg.message == WM_HOTKEY && msg.wParam.ToInt32() == HotkeyId)
            {
                try { HotkeyPressed?.Invoke(); }
                catch (Exception ex) { _logger.Error(ex, "HotkeyPressed handler threw"); }
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
        try { _cts?.Cancel(); } catch { /* ignore */ }
        // Give the message pump a moment to exit; the thread is a background thread
        // so it won't keep the process alive.
        _thread?.Join(TimeSpan.FromSeconds(1));
        _cts?.Dispose();
    }

    // --- Win32 ---

    private static readonly IntPtr HWND_MESSAGE = new(-3);
    private const uint PM_NOREMOVE = 0x0000;

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint removeMsg);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG msg);
}
