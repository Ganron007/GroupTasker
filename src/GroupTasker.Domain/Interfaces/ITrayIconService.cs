namespace GroupTasker.Domain.Interfaces;

/// <summary>
/// A system tray (notification area) icon with a context menu. Raised events
/// are delivered on the tray's own message-pump thread; subscribers that touch
/// UI must marshal to the UI thread.
/// </summary>
public interface ITrayIconService : IDisposable
{
    /// <summary>Show the tray icon with the given tooltip text.</summary>
    void Show(string tooltipText);

    /// <summary>Hide and remove the tray icon.</summary>
    void Hide();

    /// <summary>Replace the context menu items shown on right-click.</summary>
    void SetMenu(IReadOnlyList<TrayMenuItem> items);

    /// <summary>Update the tooltip text without recreating the icon.</summary>
    void SetTooltip(string tooltipText);

    /// <summary>Raised when the user left-clicks the tray icon (open the primary group flyout).</summary>
    event Action? IconClicked;

    /// <summary>Raised when the user selects a menu item. <see cref="TrayMenuItem.ActionKey"/> identifies which item.</summary>
    event Action<string>? MenuAction;
}

/// <summary>One entry in the tray context menu. Separators have a blank label and action key.</summary>
public sealed record TrayMenuItem(string Label, string ActionKey)
{
    public bool IsSeparator => Label.Length == 0 && ActionKey.Length == 0;
    public bool IsChecked { get; init; }
}
