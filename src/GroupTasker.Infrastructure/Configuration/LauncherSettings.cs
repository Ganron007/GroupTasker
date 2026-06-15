using GroupTasker.Domain.Entities;

namespace GroupTasker.Infrastructure.Configuration;

public sealed class LauncherSettings
{
    public bool HasPosition { get; set; }
    public int PositionX { get; set; }
    public int PositionY { get; set; }

    /// <summary>Group to open when the global hotkey is pressed. <c>null</c> = fall back to the first group by CreatedAt.</summary>
    public Guid? PrimaryGroupId { get; set; }

    /// <summary>Global hotkey that opens the primary group. <c>null</c> = hotkey disabled.</summary>
    public HotkeyBinding? PrimaryGroupHotkey { get; set; }
}
