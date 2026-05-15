namespace GroupTasker.Domain.Entities;

/// <summary>
/// Represents a single launchable item within a group.
/// </summary>
public sealed class Shortcut
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The path the user originally provided (exe, lnk, folder, or store app id).</summary>
    public string SourcePath { get; init; } = string.Empty;

    /// <summary>The resolved target path (for .lnk files) or the same as SourcePath for direct targets.</summary>
    public string? TargetPath { get; set; }

    /// <summary>Human-readable name for display (usually the filename without extension).</summary>
    public string DisplayName { get; set; } = string.Empty;

    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public ShortcutType Type { get; set; } = ShortcutType.Unknown;
    public string? IconPath { get; set; }
    public bool IsVisible { get; set; } = true;
    public int SortOrder { get; set; }
}
