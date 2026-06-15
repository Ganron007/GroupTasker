using System.IO;
using GroupTasker.Domain.Entities;
using GroupTasker.Infrastructure.Data;
using GroupTasker.UnitTests.Application;

namespace GroupTasker.UnitTests.Infrastructure;

public class JsonGroupRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gt-test-" + Guid.NewGuid().ToString("N"));
    private readonly JsonGroupRepository _repo;

    public JsonGroupRepositoryTests()
    {
        Directory.CreateDirectory(_root);
        _repo = new JsonGroupRepository(new FakeConfigPathProvider(_root));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp cleanup best-effort */ }
    }

    [Fact]
    public async Task SaveAndGetById_RoundTrips()
    {
        var group = new Group { Name = "Tools" };
        group.AddShortcut(new Shortcut
        {
            SourcePath = @"C:\Apps\foo.exe",
            DisplayName = "Foo",
            Type = ShortcutType.Application
        });
        group.AddShortcut(new Shortcut
        {
            SourcePath = "Claude_pzs8sxrjxfjjc!Claude",
            DisplayName = "Claude",
            Type = ShortcutType.LiveApplication
        });

        await _repo.SaveAsync(group);
        var loaded = await _repo.GetByIdAsync(group.Id);

        Assert.NotNull(loaded);
        Assert.Equal(group.Id, loaded!.Id);
        Assert.Equal("Tools", loaded.Name);
        Assert.Equal(2, loaded.Shortcuts.Count);
        Assert.Equal("Foo", loaded.Shortcuts[0].DisplayName);
        Assert.Equal(ShortcutType.LiveApplication, loaded.Shortcuts[1].Type);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsMultipleGroups_OrderedByCreatedAt()
    {
        var g1 = new Group { Name = "Alpha" };
        var g2 = new Group { Name = "Beta" };

        await Task.Delay(50);
        var g3 = new Group { Name = "Gamma" };

        await _repo.SaveAsync(g3);
        await _repo.SaveAsync(g1);
        await _repo.SaveAsync(g2);

        var all = await _repo.GetAllAsync();

        Assert.Equal(3, all.Count);
        Assert.Equal("Alpha", all[0].Name);
        Assert.Equal("Beta", all[1].Name);
        Assert.Equal("Gamma", all[2].Name);
    }

    [Fact]
    public async Task GetAllAsync_NoGroupsDirectory_ReturnsEmpty()
    {
        var all = await _repo.GetAllAsync();
        Assert.Empty(all);
    }

    [Fact]
    public async Task GetByIdAsync_Nonexistent_ReturnsNull()
    {
        var loaded = await _repo.GetByIdAsync(Guid.NewGuid());
        Assert.Null(loaded);
    }

    [Fact]
    public async Task DeleteAsync_RemovesGroupDirectory()
    {
        var group = new Group { Name = "ToDelete" };
        await _repo.SaveAsync(group);

        var deleted = await _repo.DeleteAsync(group.Id);

        Assert.True(deleted);
        Assert.False(Directory.Exists(Path.Combine(_root, "groups", group.Id.ToString("N"))));
    }

    [Fact]
    public async Task DeleteAsync_Nonexistent_ReturnsFalse()
    {
        var deleted = await _repo.DeleteAsync(Guid.NewGuid());
        Assert.False(deleted);
    }

    [Fact]
    public async Task SaveAsync_Twice_OverwritesIdempotently()
    {
        var group = new Group { Name = "Once" };
        await _repo.SaveAsync(group);

        group.AddShortcut(new Shortcut { SourcePath = "a.exe", DisplayName = "A", Type = ShortcutType.Application });
        await _repo.SaveAsync(group);

        var loaded = await _repo.GetByIdAsync(group.Id);
        Assert.NotNull(loaded);
        Assert.Single(loaded!.Shortcuts);
        Assert.Equal("Once", loaded.Name);
    }

    [Fact]
    public async Task GetByIdAsync_CorruptJson_ReturnsNull()
    {
        var group = new Group { Name = "Corrupt" };
        await _repo.SaveAsync(group);

        var groupDir = Path.Combine(_root, "groups", group.Id.ToString("N"));
        await File.WriteAllTextAsync(Path.Combine(groupDir, "group.json"), "{ this is not valid json }");

        var loaded = await _repo.GetByIdAsync(group.Id);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task GetAllAsync_SkipsCorruptGroups()
    {
        var good = new Group { Name = "Good" };
        await _repo.SaveAsync(good);

        var badDir = Path.Combine(_root, "groups", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(badDir);
        await File.WriteAllTextAsync(Path.Combine(badDir, "group.json"), "not json");

        var all = await _repo.GetAllAsync();

        Assert.Single(all);
        Assert.Equal("Good", all[0].Name);
    }
}
