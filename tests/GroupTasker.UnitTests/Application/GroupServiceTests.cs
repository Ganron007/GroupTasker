using GroupTasker.Application.Services;
using GroupTasker.Domain.ValueObjects;

namespace GroupTasker.UnitTests.Application;

public class GroupServiceTests : IDisposable
{
    private readonly string _root;
    private readonly InMemoryGroupRepository _repo = new();
    private readonly FakeIconCacheService _iconCache = new();
    private readonly FakeShortcutService _shortcutSvc = new();
    private readonly FakeShellGateway _shell = new();
    private readonly GroupService _svc;

    public GroupServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"gt-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        var paths = new FakeConfigPathProvider(_root);
        _svc = new GroupService(_repo, _iconCache, _shortcutSvc, paths, _shell);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task CreateGroupAsync_WithShortcuts_BuildsIconsOnce_AndSavesOnce()
    {
        var shortcuts = new[]
        {
            _shortcutSvc.Resolve("a.exe"),
            _shortcutSvc.Resolve("b.exe")
        };

        var group = await _svc.CreateGroupAsync("Tools", shortcuts);

        Assert.Equal("Tools", group.Name);
        Assert.Equal(2, group.Shortcuts.Count);
        // Old code path saved twice (create → mutate → save again). New path saves once.
        Assert.Equal(1, _repo.SaveCallCount);
        Assert.Equal(1, _iconCache.BuildGroupIconCalls);
        Assert.False(group.IconCacheDirty);
    }

    [Fact]
    public async Task SaveGroupAsync_SkipsIconRebuild_WhenCacheClean()
    {
        var group = await _svc.CreateGroupAsync("Tools", new[] { _shortcutSvc.Resolve("a.exe") });
        var calls = _iconCache.BuildGroupIconCalls;

        // Save again with no changes — IconCacheDirty is false, so no rebuild.
        // We also have to fake the on-disk presence of the group icon to take the fast path.
        Directory.CreateDirectory(Path.Combine(_root, "groups", group.Id.ToString("N")));
        File.WriteAllBytes(group.IconPath!, Array.Empty<byte>());

        await _svc.SaveGroupAsync(group);
        Assert.Equal(calls, _iconCache.BuildGroupIconCalls);
    }

    [Fact]
    public async Task SaveGroupAsync_RebuildsIcon_WhenCacheDirty()
    {
        var group = await _svc.CreateGroupAsync("Tools", new[] { _shortcutSvc.Resolve("a.exe") });
        var calls = _iconCache.BuildGroupIconCalls;

        // Mark dirty (simulate the user adding/removing a shortcut)
        group.AddShortcut(_shortcutSvc.Resolve("b.exe"));
        await _svc.SaveGroupAsync(group);

        Assert.Equal(calls + 1, _iconCache.BuildGroupIconCalls);
    }

    [Fact]
    public async Task PinGroupToTaskbarAsync_OnSuccess_ReturnsPinned_AndRevealsFolder()
    {
        _shortcutSvc.PinSucceeds = true;
        var group = await _svc.CreateGroupAsync("Tools", new[] { _shortcutSvc.Resolve("a.exe") });

        var result = await _svc.PinGroupToTaskbarAsync(group.Id);

        Assert.Equal(PinOutcome.Pinned, result.Outcome);
        Assert.NotEmpty(result.LauncherPath);
        // FakeShellGateway records every reveal regardless of folder existence,
        // so we're really asserting GroupService dispatched the call.
        Assert.Single(_shell.RevealedFolders);
    }

    [Fact]
    public async Task PinGroupToTaskbarAsync_OnPinFailure_ReturnsManualPinRequired()
    {
        _shortcutSvc.PinSucceeds = false;
        var group = await _svc.CreateGroupAsync("Tools", new[] { _shortcutSvc.Resolve("a.exe") });

        var result = await _svc.PinGroupToTaskbarAsync(group.Id);

        Assert.Equal(PinOutcome.ShortcutCreatedManualPinRequired, result.Outcome);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task AddShortcutAsync_UsesShortcutServiceResolve_AndPersists()
    {
        var group = await _svc.CreateGroupAsync("Tools", null);

        await _svc.AddShortcutAsync(group.Id, "newapp.exe");

        var reloaded = await _svc.GetGroupAsync(group.Id);
        Assert.NotNull(reloaded);
        Assert.Single(reloaded!.Shortcuts);
        Assert.Equal("newapp.exe", reloaded.Shortcuts[0].SourcePath);
    }

    [Fact]
    public async Task DeleteGroupAsync_RemovesFromRepository()
    {
        var group = await _svc.CreateGroupAsync("Tools", null);
        Assert.NotNull(await _svc.GetGroupAsync(group.Id));

        await _svc.DeleteGroupAsync(group.Id);
        Assert.Null(await _svc.GetGroupAsync(group.Id));
    }

    [Fact]
    public async Task DeleteGroupAsync_ThrowsWhenGroupMissing()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.DeleteGroupAsync(Guid.NewGuid()));
    }
}
