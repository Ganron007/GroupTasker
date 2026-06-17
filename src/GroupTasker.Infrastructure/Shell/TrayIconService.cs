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
/// Why a dedicated thread? NotifyIcon is a WinForms component and needs
/// a WinForms message pump on the thread that owns it. Avalonia's UI
/// dispatcher does NOT provide a WinForms message pump, so a NotifyIcon
/// created on the Avalonia UI thread receives no callbacks and appears
/// "dead" (visible but click events go nowhere). The proven fix is to
/// create the icon on a separate STA thread that calls Application.Run().
///
/// Threading contract:
///   - Public Show/Hide/SetMenu/SetTooltip marshal to the tray thread via
///     BeginInvoke on a hidden helper form.
///   - IconClicked / MenuAction events fire on the tray thread; subscribers
///     must marshal to their own UI thread (the App already does this).
/// </summary>
public sealed class TrayIconService : ITrayIconService, IDisposable
{
    private readonly ILogger _logger;
    private readonly Thread _thread;
    private readonly TrayHostForm _host;
    private readonly ManualResetEventSlim _ready = new();

    public TrayIconService(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;

        _host = new TrayHostForm();

        _thread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "GroupTasker.TrayIcon",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        // Block until the tray thread has built the NotifyIcon. This guards
        // against races where Show() is called immediately after construction.
        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
        {
            _logger.Warning("Tray icon thread failed to initialise within 5 seconds");
        }
    }

    public event Action? IconClicked;
    public event Action<string>? MenuAction;

    public void Show(string tooltipText)
    {
        if (_host.IsDisposed) return;
        var safe = tooltipText ?? "GroupTasker";
        var text = safe.Length > 63 ? safe[..63] : safe;
        _host.BeginInvoke(() => _host.ShowIcon(text));
    }

    public void Hide()
    {
        if (_host.IsDisposed) return;
        _host.BeginInvoke(() => _host.HideIcon());
    }

    public void SetTooltip(string tooltipText) => Show(tooltipText);

    public void SetMenu(IReadOnlyList<TrayMenuItem> items)
    {
        if (_host.IsDisposed) return;
        // Snapshot to a local list because the items list may be reused.
        var snapshot = items.ToList();
        _host.BeginInvoke(() => _host.SetMenu(snapshot));
    }

    public void Dispose()
    {
        if (_host.IsDisposed) return;
        try
        {
            _host.BeginInvoke(() =>
            {
                _host.ShutdownIcon();
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
            _logger.Information("Tray icon STA thread starting (id={Id})", Thread.CurrentThread.ManagedThreadId);

            // Initialise WinForms on this thread. EnableVisualStyles has no
            // effect on a non-visual icon but is cheap to call.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.OleRequired();

            // Initialise the icon (creates the hidden message window + NotifyIcon).
            _host.Initialize(() => IconClicked?.Invoke(), key => MenuAction?.Invoke(key));
            _ready.Set();

            // Run a WinForms message loop on this STA thread. The loop exits
            // when Application.ExitThread() is called from Dispose().
            Application.Run();

            _logger.Information("Tray icon STA thread exiting");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Tray icon STA thread crashed");
        }
        finally
        {
            _host.Dispose();
        }
    }
}

/// <summary>
/// Hidden form that hosts the NotifyIcon. Lives on the dedicated STA thread
/// and is the Invoke target for all tray service calls. The form is never
/// shown; it exists only to provide a message-pump root and a thread-affine
/// Control for BeginInvoke.
/// </summary>
internal sealed class TrayHostForm : Form
{
    private NotifyIcon? _icon;
    private ContextMenuStrip? _menu;
    private Action? _onClick;
    private Action<string>? _onMenuAction;
    private readonly Dictionary<ToolStripMenuItem, string> _actionKeys = new();

    public TrayHostForm()
    {
        // No-show invisible form. FormBorderStyle=None + ShowInTaskbar=false
        // + Opacity=0 keeps it completely out of the way.
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        Opacity = 0;
        Width = Height = 0;
        StartPosition = FormStartPosition.Manual;
        Location = new System.Drawing.Point(-32000, -32000);
    }

    public void Initialize(Action onClick, Action<string> onMenuAction)
    {
        _onClick = onClick;
        _onMenuAction = onMenuAction;

        // Load icon: try the running exe's embedded icon, fall back to system app icon.
        Icon trayIcon;
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                var extracted = Icon.ExtractAssociatedIcon(path);
                trayIcon = extracted ?? SystemIcons.Application;
            }
            else
            {
                trayIcon = SystemIcons.Application;
            }
        }
        catch
        {
            trayIcon = SystemIcons.Application;
        }

        _menu = new ContextMenuStrip();
        _icon = new NotifyIcon
        {
            Icon = trayIcon,
            Visible = false,
            Text = "GroupTasker",
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                try { _onClick?.Invoke(); }
                catch (Exception) { /* swallowed on purpose; logged in caller */ }
            }
        };
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                try { _onClick?.Invoke(); }
                catch (Exception) { /* swallowed on purpose; logged in caller */ }
            }
        };
        _icon.ContextMenuStrip = _menu;
    }

    public new void ShowIcon(string tooltip)
    {
        if (_icon is null) return;
        _icon.Text = tooltip;
        _icon.Visible = true;
    }

    public void HideIcon()
    {
        if (_icon is null) return;
        _icon.Visible = false;
    }

    public void ShutdownIcon()
    {
        if (_icon is null) return;
        _icon.Visible = false;
        _icon.Dispose();
        _icon = null;
        _menu?.Dispose();
        _menu = null;
    }

    public void SetMenu(IReadOnlyList<TrayMenuItem> items)
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
                catch (Exception) { /* swallowed on purpose */ }
            };
            _actionKeys[item] = key;
            _menu.Items.Add(item);
        }

        if (groups.Count > 0)
            _menu.Items.Add(new ToolStripSeparator());

        // Fixed trailing items: Open configurator, Exit.
        var openItem = new ToolStripMenuItem("Open");
        openItem.Click += (_, _) =>
        {
            try { _onMenuAction?.Invoke("open-configurator"); }
            catch (Exception) { /* swallowed on purpose */ }
        };
        _menu.Items.Add(openItem);

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            try { _onMenuAction?.Invoke("quit"); }
            catch (Exception) { /* swallowed on purpose */ }
        };
        _menu.Items.Add(exitItem);

        _menu.ResumeLayout();
    }
}
