using GroupTasker.Domain.Entities;
using GroupTasker.Domain.Interfaces;

namespace GroupTasker.UnitTests.Application;

internal sealed class InMemoryGroupRepository : IGroupRepository
{
    public Dictionary<Guid, Group> Store { get; } = new();
    public int SaveCallCount;

    public Task<IReadOnlyList<Group>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Group>>(Store.Values.OrderBy(g => g.CreatedAt).ToList());

    public Task<Group?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(Store.TryGetValue(id, out var g) ? g : null);

    public Task SaveAsync(Group group, CancellationToken ct = default)
    {
        SaveCallCount++;
        Store[group.Id] = group;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(Store.Remove(id));
}

internal sealed class FakeIconCacheService : IIconCacheService
{
    public int BuildGroupIconCalls;
    public int GetIconPathCalls;
    public int RebuildCalls;

    public Task<string> GetIconPathAsync(Shortcut shortcut, string groupPath, CancellationToken ct = default)
    {
        GetIconPathCalls++;
        return Task.FromResult(Path.Combine(groupPath, "Icons", $"{shortcut.Id:N}.png"));
    }

    public Task<string> BuildGroupIconAsync(Group group, string groupPath, CancellationToken ct = default)
    {
        BuildGroupIconCalls++;
        return Task.FromResult(Path.Combine(groupPath, "GroupIcon.ico"));
    }

    public Task RebuildCacheAsync(Group group, string groupPath, CancellationToken ct = default)
    {
        RebuildCalls++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeShortcutService : IShortcutService
{
    public int PinCalls;
    public int LaunchCalls;
    public bool PinSucceeds { get; set; } = true;
    public string? LastCreatedLauncherPath;

    public Shortcut Resolve(string sourcePath) => new()
    {
        SourcePath = sourcePath,
        TargetPath = sourcePath,
        DisplayName = Path.GetFileNameWithoutExtension(sourcePath),
        Type = ShortcutType.Application
    };

    public void Launch(Shortcut shortcut) => LaunchCalls++;
    public string CreateTempLink(Shortcut shortcut) => Path.GetTempFileName() + ".lnk";

    public string CreateGroupLauncherLink(Group group, string iconPath)
    {
        LastCreatedLauncherPath = Path.Combine(Path.GetTempPath(), $"{group.Id:N}.lnk");
        return LastCreatedLauncherPath;
    }

    public bool PinToTaskbar(Group group, string launcherPath)
    {
        PinCalls++;
        return PinSucceeds;
    }
}

internal sealed class FakeConfigPathProvider : IConfigPathProvider
{
    public string ConfigRoot { get; }
    public string ShortcutFolder => Path.Combine(ConfigRoot, "shortcut");

    public FakeConfigPathProvider(string root)
    {
        ConfigRoot = root;
    }

    public string GetGroupPath(Guid groupId) => Path.Combine(ConfigRoot, "groups", groupId.ToString("N"));
}

internal sealed class FakeShellGateway : IShellGateway
{
    public List<string> RevealedFolders { get; } = new();
    public void RevealInFileManager(string folderPath) => RevealedFolders.Add(folderPath);
}
