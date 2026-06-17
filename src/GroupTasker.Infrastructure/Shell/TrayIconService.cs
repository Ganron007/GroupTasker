using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using GroupTasker.Domain.Interfaces;
using GroupTasker.Domain.Logging;

namespace GroupTasker.Infrastructure.Shell;

/// <summary>
/// Windows Forms <see cref="NotifyIcon"/> implementation. WinForms handles
/// all the P/Invoke (Shell_NotifyIcon, hidden message window, message
/// pump) internally, which is dramatically more reliable than rolling
/// our own. The Avalonia UI thread hosts the NotifyIcon without
/// conflict because NotifyIcon is a non-visual native control.
/// </summary>
public sealed class TrayIconService : ITrayIconService, IDisposable
{
    private readonly ILogger _logger;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _openItem = new("Open");
    private readonly ToolStripMenuItem _exitItem = new("Exit");
    private readonly Dictionary<ToolStripMenuItem, string> _actionKeys = new();
    private bool _disposed;
    private string _tooltip = "GroupTasker";

    public TrayIconService(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;

        _notifyIcon = new NotifyIcon
        {
            Text = _tooltip,
            Icon = LoadIcon(),
            Visible = true,
        };

        _notifyIcon.MouseClick += OnMouseClick;
        _notifyIcon.MouseDoubleClick += OnMouseDoubleClick;

        _openItem.Click += (_, _) => FireMenuAction("open-configurator");
        _exitItem.Click += (_, _) => FireMenuAction("quit");
        _menu.Items.Add(_openItem);
        _menu.Items.Add(_exitItem);
        _notifyIcon.ContextMenuStrip = _menu;

        _logger.Information("Tray icon initialised (icon={IconHandle})", _notifyIcon.Icon?.Handle ?? IntPtr.Zero);
    }

    public event Action? IconClicked;
    public event Action<string>? MenuAction;

    public void Show(string tooltipText)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TrayIconService));
        _tooltip = tooltipText ?? "GroupTasker";
        // WinForms NotifyIcon has a 63-char tooltip limit.
        _notifyIcon.Text = _tooltip.Length > 63 ? _tooltip[..63] : _tooltip;
        _notifyIcon.Visible = true;
    }

    public void Hide() => _notifyIcon.Visible = false;

    public void SetTooltip(string tooltipText) => Show(tooltipText);

    public void SetMenu(IReadOnlyList<TrayMenuItem> items)
    {
        // Rebuild the menu: separator, group items, separator, Open, Exit.
        _menu.SuspendLayout();
        _menu.Items.Clear();
        _actionKeys.Clear();

        // Group items (with primary checked).
        var groups = items.Where(i => i.ActionKey.StartsWith("group:", StringComparison.Ordinal)).ToList();
        foreach (var g in groups)
        {
            var item = new ToolStripMenuItem(g.Label)
            {
                Checked = g.IsChecked,
                CheckOnClick = false
            };
            var key = g.ActionKey;
            item.Click += (_, _) => FireMenuAction(key);
            _actionKeys[item] = key;
            _menu.Items.Add(item);
        }

        if (groups.Count > 0)
            _menu.Items.Add(new ToolStripSeparator());

        _menu.Items.Add(_openItem);
        _menu.Items.Add(_exitItem);

        _menu.ResumeLayout();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.MouseClick -= OnMouseClick;
            _notifyIcon.MouseDoubleClick -= OnMouseDoubleClick;
            _notifyIcon.Dispose();
            _menu.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error disposing tray icon");
        }
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        try
        {
            if (e.Button == MouseButtons.Left)
                IconClicked?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "TrayIcon IconClicked handler threw");
        }
    }

    private void OnMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        try
        {
            if (e.Button == MouseButtons.Left)
                IconClicked?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "TrayIcon IconClicked (dblclick) handler threw");
        }
    }

    private void FireMenuAction(string actionKey)
    {
        try
        {
            MenuAction?.Invoke(actionKey);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "TrayIcon MenuAction handler threw for {ActionKey}", actionKey);
        }
    }

    /// <summary>
    /// Load the app icon. Tries (in order): the GroupTasker.ico embedded in
    /// the running exe's manifest resources, the SystemIcons.Application icon.
    /// Never returns null — the app icon always shows.
    /// </summary>
    private static Icon LoadIcon()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath is not null)
            {
                var extracted = Icon.ExtractAssociatedIcon(exePath);
                if (extracted is not null) return extracted;
            }
        }
        catch { /* fall through */ }
        return SystemIcons.Application;
    }
}
