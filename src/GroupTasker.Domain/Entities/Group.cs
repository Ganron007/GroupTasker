using System.Text.Json.Serialization;

namespace GroupTasker.Domain.Entities;

/// <summary>
/// Aggregate root representing a named group of shortcuts that can be pinned to the taskbar.
/// </summary>
public sealed class Group
{
    private string _name = "New Group";

    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Display name. Control characters and double-quotes are rejected so the name is
    /// safe to use in inter-process protocols (see <c>SingleInstanceService</c>), in
    /// shortcut file names, and embedded in <c>.lnk</c> arguments.
    /// </summary>
    public string Name
    {
        get => _name;
        set => _name = ValidateName(value);
    }

    public string? IconPath { get; set; }

    /// <summary>
    /// Shortcuts belonging to this group. Round-trips via <see cref="JsonIncludeAttribute"/>
    /// so the deserializer can populate the collection on init-only entities.
    /// </summary>
    [JsonInclude]
    public List<Shortcut> Shortcuts { get; private set; } = [];

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public bool IconCacheDirty { get; set; } = true;

    // --- Domain logic ---

    public void AddShortcut(Shortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        Shortcuts.Add(shortcut);
        shortcut.SortOrder = Shortcuts.Count - 1;
        MarkModified();
    }

    public bool RemoveShortcut(Guid shortcutId)
    {
        var removed = Shortcuts.RemoveAll(s => s.Id == shortcutId) > 0;
        if (removed)
        {
            // Renumber to keep SortOrder dense.
            for (var i = 0; i < Shortcuts.Count; i++)
                Shortcuts[i].SortOrder = i;
            MarkModified();
        }
        return removed;
    }

    public void ReorderShortcut(Guid shortcutId, int newIndex)
    {
        var shortcut = Shortcuts.Find(s => s.Id == shortcutId);
        if (shortcut is null) return;

        Shortcuts.Remove(shortcut);
        Shortcuts.Insert(Math.Clamp(newIndex, 0, Shortcuts.Count), shortcut);

        for (var i = 0; i < Shortcuts.Count; i++)
            Shortcuts[i].SortOrder = i;

        MarkModified();
    }

    /// <summary>
    /// Replace the entire shortcut collection in one step. Used by the editor when the user
    /// reshuffles or removes multiple shortcuts at once. Maintains <see cref="MarkModified"/>
    /// semantics — callers should not mutate <see cref="Shortcuts"/> directly.
    /// </summary>
    public void ReplaceShortcuts(IEnumerable<Shortcut> shortcuts)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        Shortcuts.Clear();
        var i = 0;
        foreach (var s in shortcuts)
        {
            s.SortOrder = i++;
            Shortcuts.Add(s);
        }
        MarkModified();
    }

    public void MarkIconCacheClean() => IconCacheDirty = false;

    private void MarkModified()
    {
        ModifiedAt = DateTime.UtcNow;
        IconCacheDirty = true;
    }

    private static string ValidateName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("Group name cannot be empty.", nameof(value));

        foreach (var c in trimmed)
        {
            if (char.IsControl(c))
                throw new ArgumentException("Group name cannot contain control characters.", nameof(value));
            if (c == '"')
                throw new ArgumentException("Group name cannot contain double-quote characters.", nameof(value));
        }

        return trimmed;
    }
}
