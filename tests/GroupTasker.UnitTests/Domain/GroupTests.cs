using GroupTasker.Domain.Entities;

namespace GroupTasker.UnitTests.Domain;

public class GroupTests
{
    [Fact]
    public void AddShortcut_AssignsSortOrder_AndMarksDirty()
    {
        var group = new Group { Name = "Tools" };
        group.IconCacheDirty = false; // simulate a cleaned state
        var s1 = new Shortcut { SourcePath = "a.exe" };
        var s2 = new Shortcut { SourcePath = "b.exe" };

        group.AddShortcut(s1);
        group.AddShortcut(s2);

        Assert.Equal(0, s1.SortOrder);
        Assert.Equal(1, s2.SortOrder);
        Assert.True(group.IconCacheDirty);
    }

    [Fact]
    public void RemoveShortcut_RenumbersRemaining()
    {
        var group = new Group { Name = "Tools" };
        var s1 = new Shortcut { SourcePath = "a.exe" };
        var s2 = new Shortcut { SourcePath = "b.exe" };
        var s3 = new Shortcut { SourcePath = "c.exe" };
        group.AddShortcut(s1);
        group.AddShortcut(s2);
        group.AddShortcut(s3);

        Assert.True(group.RemoveShortcut(s2.Id));

        Assert.Equal(2, group.Shortcuts.Count);
        Assert.Equal(0, s1.SortOrder);
        Assert.Equal(1, s3.SortOrder);
    }

    [Fact]
    public void RemoveShortcut_ReturnsFalse_WhenIdMissing()
    {
        var group = new Group { Name = "Tools" };
        group.AddShortcut(new Shortcut { SourcePath = "a.exe" });

        Assert.False(group.RemoveShortcut(Guid.NewGuid()));
    }

    [Fact]
    public void ReorderShortcut_MovesAndRenumbers()
    {
        var group = new Group { Name = "Tools" };
        var s1 = new Shortcut { SourcePath = "a.exe" };
        var s2 = new Shortcut { SourcePath = "b.exe" };
        var s3 = new Shortcut { SourcePath = "c.exe" };
        group.AddShortcut(s1);
        group.AddShortcut(s2);
        group.AddShortcut(s3);

        group.ReorderShortcut(s3.Id, 0);

        Assert.Equal(s3, group.Shortcuts[0]);
        Assert.Equal(0, s3.SortOrder);
        Assert.Equal(1, s1.SortOrder);
        Assert.Equal(2, s2.SortOrder);
    }

    [Fact]
    public void ReplaceShortcuts_ResetsCollection_AndAssignsSortOrder()
    {
        var group = new Group { Name = "Tools" };
        group.AddShortcut(new Shortcut { SourcePath = "old1.exe" });
        group.AddShortcut(new Shortcut { SourcePath = "old2.exe" });

        var replacement = new[]
        {
            new Shortcut { SourcePath = "new1.exe" },
            new Shortcut { SourcePath = "new2.exe" },
            new Shortcut { SourcePath = "new3.exe" },
        };

        group.ReplaceShortcuts(replacement);

        Assert.Equal(3, group.Shortcuts.Count);
        Assert.Equal(0, group.Shortcuts[0].SortOrder);
        Assert.Equal(2, group.Shortcuts[2].SortOrder);
        Assert.True(group.IconCacheDirty);
    }

    [Fact]
    public void NameSetter_TrimsWhitespace()
    {
        var group = new Group { Name = "  My Group  " };
        Assert.Equal("My Group", group.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NameSetter_RejectsEmpty(string bad)
    {
        Assert.Throws<ArgumentException>(() => new Group { Name = bad });
    }

    [Fact]
    public void NameSetter_RejectsControlChars()
    {
        Assert.Throws<ArgumentException>(() => new Group { Name = "Foo\nBar" });
    }

    [Fact]
    public void MarkIconCacheClean_ClearsDirty()
    {
        var group = new Group { Name = "Tools" };
        group.AddShortcut(new Shortcut { SourcePath = "a.exe" });
        Assert.True(group.IconCacheDirty);

        group.MarkIconCacheClean();
        Assert.False(group.IconCacheDirty);
    }
}
