using System.Runtime.InteropServices;

namespace GroupTasker.Infrastructure.Shell;

/// <summary>
/// Win32 interop for taskbar detection and cursor positioning.
/// Multi-monitor aware: bounds come from the monitor that owns the taskbar / cursor
/// (via <c>MonitorFromPoint</c>), not from the primary monitor.
/// </summary>
public static class TaskbarHelper
{
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint ABM_GETTASKBARPOS = 0x00000005;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    public enum TaskbarEdge : uint { Left = 0, Top = 1, Right = 2, Bottom = 3 }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    public sealed record TaskbarInfo(int X, int Y, int Width, int Height, TaskbarEdge Edge, bool IsValid);
    public sealed record ScreenBounds(int X, int Y, int Width, int Height);

    public static TaskbarInfo GetTaskbarInfo()
    {
        var abd = new APPBARDATA();
        abd.cbSize = (uint)Marshal.SizeOf<APPBARDATA>();
        var result = SHAppBarMessage(ABM_GETTASKBARPOS, ref abd);
        var ok = result != IntPtr.Zero;

        return new TaskbarInfo(
            abd.rc.Left,
            abd.rc.Top,
            abd.rc.Right - abd.rc.Left,
            abd.rc.Bottom - abd.rc.Top,
            (TaskbarEdge)abd.uEdge,
            ok);
    }

    public static (int X, int Y) GetCursorPos()
    {
        if (GetCursorPos(out var pt)) return (pt.X, pt.Y);
        return (0, 0);
    }

    /// <summary>
    /// Bounds of the monitor that contains the given screen point. Falls back to primary
    /// screen size via <c>GetSystemMetrics</c> if <c>MonitorFromPoint</c> fails.
    /// </summary>
    private static ScreenBounds GetMonitorBoundsFor(int x, int y)
    {
        var pt = new POINT { X = x, Y = y };
        var hMon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        if (hMon != IntPtr.Zero)
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(hMon, ref mi))
            {
                return new ScreenBounds(
                    mi.rcMonitor.Left,
                    mi.rcMonitor.Top,
                    mi.rcMonitor.Right - mi.rcMonitor.Left,
                    mi.rcMonitor.Bottom - mi.rcMonitor.Top);
            }
        }

        return new ScreenBounds(0, 0, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
    }

    public static (int X, int Y) GetDefaultPosition(int windowWidth, int windowHeight)
    {
        var taskbar = GetTaskbarInfo();
        if (!taskbar.IsValid)
        {
            var b = GetMonitorBoundsFor(0, 0);
            return (b.X + (b.Width - windowWidth) / 2, b.Y + (b.Height - windowHeight) / 2);
        }

        var screen = GetMonitorBoundsFor(taskbar.X + taskbar.Width / 2, taskbar.Y + taskbar.Height / 2);
        return ClampToScreen(PlaceAdjacentToTaskbar(taskbar, screen, windowWidth, windowHeight, useCursor: false, 0, 0),
                             screen, windowWidth, windowHeight);
    }

    public static (int X, int Y) CalculateFlyoutPosition(int windowWidth, int windowHeight)
    {
        var taskbar = GetTaskbarInfo();
        var (cursorX, cursorY) = GetCursorPos();

        if (!taskbar.IsValid)
        {
            var b = GetMonitorBoundsFor(cursorX, cursorY);
            return (b.X + (b.Width - windowWidth) / 2, b.Y + (b.Height - windowHeight) / 2);
        }

        var screen = GetMonitorBoundsFor(taskbar.X + taskbar.Width / 2, taskbar.Y + taskbar.Height / 2);

        const int proximityThreshold = 200;
        bool cursorNearTaskbar = taskbar.Edge switch
        {
            TaskbarEdge.Bottom => cursorY >= taskbar.Y - proximityThreshold,
            TaskbarEdge.Top    => cursorY <= taskbar.Y + taskbar.Height + proximityThreshold,
            TaskbarEdge.Left   => cursorX <= taskbar.X + taskbar.Width + proximityThreshold,
            TaskbarEdge.Right  => cursorX >= taskbar.X - proximityThreshold,
            _                  => false
        };

        var (x, y) = PlaceAdjacentToTaskbar(taskbar, screen, windowWidth, windowHeight,
            useCursor: cursorNearTaskbar, cursorX, cursorY);
        return ClampToScreen((x, y), screen, windowWidth, windowHeight);
    }

    private static (int X, int Y) PlaceAdjacentToTaskbar(
        TaskbarInfo taskbar, ScreenBounds screen, int windowWidth, int windowHeight,
        bool useCursor, int cursorX, int cursorY)
    {
        const int gap = 4;
        int x, y;

        switch (taskbar.Edge)
        {
            case TaskbarEdge.Bottom:
                x = useCursor ? cursorX - windowWidth / 2 : screen.X + (screen.Width - windowWidth) / 2;
                y = taskbar.Y - windowHeight - gap;
                break;
            case TaskbarEdge.Top:
                x = useCursor ? cursorX - windowWidth / 2 : screen.X + (screen.Width - windowWidth) / 2;
                y = taskbar.Y + taskbar.Height + gap;
                break;
            case TaskbarEdge.Left:
                x = taskbar.X + taskbar.Width + gap;
                y = useCursor ? cursorY - windowHeight / 2 : screen.Y + (screen.Height - windowHeight) / 2;
                break;
            case TaskbarEdge.Right:
                x = taskbar.X - windowWidth - gap;
                y = useCursor ? cursorY - windowHeight / 2 : screen.Y + (screen.Height - windowHeight) / 2;
                break;
            default:
                x = screen.X + (screen.Width - windowWidth) / 2;
                y = screen.Y + (screen.Height - windowHeight) / 2;
                break;
        }

        return (x, y);
    }

    private static (int X, int Y) ClampToScreen((int X, int Y) pos, ScreenBounds s, int w, int h)
    {
        var x = Math.Max(s.X, Math.Min(pos.X, s.X + s.Width - w));
        var y = Math.Max(s.Y, Math.Min(pos.Y, s.Y + s.Height - h));
        return (x, y);
    }
}
