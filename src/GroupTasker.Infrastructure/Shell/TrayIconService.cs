using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using GroupTasker.Domain.Interfaces;
using GroupTasker.Domain.Logging;

namespace GroupTasker.Infrastructure.Shell;

/// <summary>
/// System tray icon backed by WinForms <see cref="NotifyIcon"/>, hosted on
/// a dedicated STA thread that runs its own WinForms message loop.
///
/// The host form is created ON the STA thread (not the main thread) so its
/// handle lives there. This is the critical bit: <see cref="Form.BeginInvoke"/>
/// posts to whichever thread owns the form's handle, so if the form's handle
/// is on the main thread (which has no WinForms message pump) the posted
/// messages are silently dropped and the icon never becomes visible.
/// <see cref="Application.Run(Form)"/> creates the handle on the calling thread
/// and pumps messages for it, so clicks on the NotifyIcon are dispatched.
/// </summary>
public sealed class TrayIconService : ITrayIconService, IDisposable
{
    private readonly ILogger _logger;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private volatile Form? _host;
    private volatile bool _disposed;

    public TrayIconService(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;

        _thread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "GroupTasker.TrayIcon",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        // Block until the STA thread has built the form + NotifyIcon, so the
        // public methods below can rely on _host being non-null.
        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
            _logger.Warning("Tray icon thread failed to initialise within 5 seconds");
    }

    public event Action? IconClicked;
    public event Action<string>? MenuAction;

    public void Show(string tooltipText)
    {
        var host = _host as TrayHostForm;
        if (_disposed || host is null || host.IsDisposed) return;
        var safe = tooltipText ?? "GroupTasker";
        var text = safe.Length > 63 ? safe[..63] : safe;
        host.BeginInvoke(() => host.SetTrayIcon(text));
    }

    public void Hide()
    {
        var host = _host as TrayHostForm;
        if (host is null || host.IsDisposed) return;
        host.BeginInvoke(() => host.HideTrayIcon());
    }

    public void SetTooltip(string tooltipText) => Show(tooltipText);

    public void SetMenu(IReadOnlyList<TrayMenuItem> items)
    {
        var host = _host as TrayHostForm;
        if (host is null || host.IsDisposed) return;
        var snapshot = items.ToList();
        host.BeginInvoke(() => host.SetTrayMenu(snapshot));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var host = _host as TrayHostForm;
        if (host is null || host.IsDisposed) return;
        try
        {
            host.BeginInvoke(() =>
            {
                host.ShutdownTrayIcon();
                Application.ExitThread();
            });
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error initiating tray thread shutdown");
        }
    }

    private void MessageLoop()
    {
        try
        {
            _logger.Information("Tray icon STA thread starting (id={ThreadId})", Thread.CurrentThread.ManagedThreadId);

            Application.EnableVisualStyles();
            Application.OleRequired();

            // Create the form ON this STA thread. Application.Run(form) below
            // creates the form's handle on this same thread, so cross-thread
            // BeginInvoke posts to this thread's message queue (which IS being
            // pumped by Application.Run).
            var host = new TrayHostForm();
            host.Initialize(() => IconClicked?.Invoke(), key => MenuAction?.Invoke(key));
            _host = host;

            _logger.Information("Tray icon ready: form={FormHandle} icon={IconHandle}",
                host.Handle, host.IconHandle);

            _ready.Set();

            // Application.Run(form) creates the form's handle on this thread
            // and pumps messages for the thread. The NotifyIcon's hidden
            // message window is also on this thread, so its callbacks are
            // dispatched correctly.
            Application.Run(host);

            _logger.Information("Tray icon STA thread exiting");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Tray icon STA thread crashed");
        }
    }
}

/// <summary>
/// Hidden form that hosts the NotifyIcon. The form is never <see cref="Form.Show"/>n;
/// it exists only to provide a thread-affine <see cref="Control"/> for
/// <see cref="Control.BeginInvoke"/> and to serve as the message-pump root
/// for <see cref="Application.Run(Form)"/>.
/// </summary>
internal sealed class TrayHostForm : Form
{
    private NotifyIcon? _icon;
    private ContextMenuStrip? _menu;
    private Action? _onClick;
    private Action<string>? _onMenuAction;
    private readonly Dictionary<ToolStripMenuItem, string> _actionKeys = new();

    public IntPtr IconHandle => _icon?.Icon?.Handle ?? IntPtr.Zero;

    public TrayHostForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        Opacity = 0;
        Width = Height = 0;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
    }

    public void Initialize(Action onClick, Action<string> onMenuAction)
    {
        _onClick = onClick;
        _onMenuAction = onMenuAction;

        _menu = new ContextMenuStrip();
        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = false,
            Text = "GroupTasker",
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                try { _onClick?.Invoke(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            }
        };
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                try { _onClick?.Invoke(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            }
        };
        _icon.ContextMenuStrip = _menu;
    }

    public void SetTrayIcon(string tooltip)
    {
        if (_icon is null) return;
        _icon.Text = tooltip;
        _icon.Visible = true;
    }

    public void HideTrayIcon()
    {
        if (_icon is null) return;
        _icon.Visible = false;
    }

    public void ShutdownTrayIcon()
    {
        if (_icon is null) return;
        _icon.Visible = false;
        _icon.Dispose();
        _icon = null;
        _menu?.Dispose();
        _menu = null;
    }

    public void SetTrayMenu(IReadOnlyList<TrayMenuItem> items)
    {
        if (_menu is null) return;

        _menu.SuspendLayout();
        _menu.Items.Clear();
        _actionKeys.Clear();

        var groups = items.Where(i => i.ActionKey.StartsWith("group:", StringComparison.Ordinal)).ToList();

        foreach (var g in groups)
        {
            var item = new ToolStripMenuItem(g.Label)
            {
                Checked = g.IsChecked,
                CheckOnClick = false,
            };
            var key = g.ActionKey;
            item.Click += (_, _) =>
            {
                try { _onMenuAction?.Invoke(key); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            };
            _actionKeys[item] = key;
            _menu.Items.Add(item);
        }

        if (groups.Count > 0)
            _menu.Items.Add(new ToolStripSeparator());

        var openItem = new ToolStripMenuItem("Open");
        openItem.Click += (_, _) =>
        {
            try { _onMenuAction?.Invoke("open-configurator"); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        };
        _menu.Items.Add(openItem);

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            try { _onMenuAction?.Invoke("quit"); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        };
        _menu.Items.Add(exitItem);

        _menu.ResumeLayout();
    }

    private static Icon LoadIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                var extracted = Icon.ExtractAssociatedIcon(path);
                if (extracted is not null) return extracted;
            }
        }
        catch { /* fall through */ }
        return SystemIcons.Application;
    }
}
