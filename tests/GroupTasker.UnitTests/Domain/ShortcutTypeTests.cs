using GroupTasker.Domain.Entities;

namespace GroupTasker.UnitTests.Domain;

public class ShortcutTypeTests
{
    [Fact]
    public void Application_IsZeroOrdinal()
    {
        // The original schema put Application at ordinal 0 and existing on-disk
        // group.json files serialise the enum as that integer. We must NOT renumber
        // the enum or every saved shortcut becomes a different type at next load.
        Assert.Equal(0, (int)ShortcutType.Application);
        Assert.Equal(1, (int)ShortcutType.Link);
        Assert.Equal(2, (int)ShortcutType.Folder);
        Assert.Equal(3, (int)ShortcutType.StoreApp);
        Assert.Equal(4, (int)ShortcutType.Unknown);
    }

    [Fact]
    public void DefaultConstructedShortcut_IsUnknown_NotApplication()
    {
        // Even though Application is the zero value, Shortcut.Type's field
        // initialiser sets the default to Unknown, so a freshly-constructed
        // entity (or a JSON document missing the field) still ends up classified
        // as Unknown rather than silently claiming to be an application.
        Assert.Equal(ShortcutType.Unknown, new Shortcut().Type);
    }
}
