namespace GroupTasker.Domain.Entities;

/// <summary>
/// Classifies what kind of target a shortcut points to.
/// </summary>
/// <remarks>
/// Ordinals are deliberately preserved from the original schema so existing
/// <c>group.json</c> files (which serialise the enum as an integer) still
/// deserialise correctly. New writes use string names via
/// <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/> registered on
/// the repository's options, so future renames won't break on-disk data.
/// <para>
/// Note: the zero value is <see cref="Application"/>, not <see cref="Unknown"/>.
/// The <c>Shortcut.Type</c> property has a default initialiser of
/// <see cref="Unknown"/> so a freshly-constructed instance or a JSON document
/// missing the field still ends up as <see cref="Unknown"/> rather than silently
/// claiming to be an application.
/// </para>
/// </remarks>
public enum ShortcutType
{
    Application = 0,
    Link = 1,
    Folder = 2,
    StoreApp = 3,
    Unknown = 4
}
