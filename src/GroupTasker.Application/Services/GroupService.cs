using System.Linq;
using GroupTasker.Domain.Entities;
using GroupTasker.Domain.Interfaces;
using GroupTasker.Domain.ValueObjects;

namespace GroupTasker.Application.Services;

/// <summary>
/// Application service that orchestrates group operations across domain and infrastructure.
/// Thin: delegates validation to the domain entity, persistence to the repository,
/// icon work to the icon cache, and shell-side actions to <see cref="IShellGateway"/>.
/// </summary>
public sealed class GroupService
{
    private readonly IGroupRepository _repository;
    private readonly IIconCacheService _iconCache;
    private readonly IShortcutService _shortcutService;
    private readonly IConfigPathProvider _paths;
    private readonly IShellGateway _shell;

    public GroupService(
        IGroupRepository repository,
        IIconCacheService iconCache,
        IShortcutService shortcutService,
        IConfigPathProvider paths,
        IShellGateway shell)
    {
        _repository = repository;
        _iconCache = iconCache;
        _shortcutService = shortcutService;
        _paths = paths;
        _shell = shell;
    }

    public Task<IReadOnlyList<Group>> GetAllGroupsAsync(CancellationToken ct = default)
        => _repository.GetAllAsync(ct);

    public Task<Group?> GetGroupAsync(Guid id, CancellationToken ct = default)
        => _repository.GetByIdAsync(id, ct);

    public async Task<Group> CreateGroupAsync(string name, IEnumerable<Shortcut>? initialShortcuts = null, CancellationToken ct = default)
    {
        var group = new Group { Name = name };

        if (initialShortcuts is not null)
        {
            foreach (var s in initialShortcuts)
                group.AddShortcut(s);
        }

        await BuildIconsIfDirtyAsync(group, ct);
        await _repository.SaveAsync(group, ct);
        return group;
    }

    public async Task DeleteGroupAsync(Guid id, CancellationToken ct = default)
    {
        var group = await _repository.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"Group {id} not found.");

        var groupPath = _paths.GetGroupPath(group.Id);
        if (Directory.Exists(groupPath))
            Directory.Delete(groupPath, recursive: true);

        await _repository.DeleteAsync(id, ct);
    }

    public async Task AddShortcutAsync(Guid groupId, string sourcePath, CancellationToken ct = default)
    {
        var group = await _repository.GetByIdAsync(groupId, ct)
            ?? throw new InvalidOperationException($"Group {groupId} not found.");

        var resolved = _shortcutService.Resolve(sourcePath);
        group.AddShortcut(resolved);

        var groupPath = _paths.GetGroupPath(group.Id);
        resolved.IconPath = await _iconCache.GetIconPathAsync(resolved, groupPath, ct);

        await _repository.SaveAsync(group, ct);
    }

    public async Task RemoveShortcutAsync(Guid groupId, Guid shortcutId, CancellationToken ct = default)
    {
        var group = await _repository.GetByIdAsync(groupId, ct)
            ?? throw new InvalidOperationException($"Group {groupId} not found.");

        if (group.RemoveShortcut(shortcutId))
            await _repository.SaveAsync(group, ct);
    }

    public async Task ReorderShortcutAsync(Guid groupId, Guid shortcutId, int newIndex, CancellationToken ct = default)
    {
        var group = await _repository.GetByIdAsync(groupId, ct)
            ?? throw new InvalidOperationException($"Group {groupId} not found.");

        group.ReorderShortcut(shortcutId, newIndex);
        await _repository.SaveAsync(group, ct);
    }

    public async Task RebuildIconCacheAsync(Guid groupId, CancellationToken ct = default)
    {
        var group = await _repository.GetByIdAsync(groupId, ct)
            ?? throw new InvalidOperationException($"Group {groupId} not found.");

        var groupPath = _paths.GetGroupPath(group.Id);
        await _iconCache.RebuildCacheAsync(group, groupPath, ct);
        group.MarkIconCacheClean();
        await _repository.SaveAsync(group, ct);
    }

    /// <summary>
    /// Save an existing group with its full shortcut list. Builds icons only if
    /// <see cref="Group.IconCacheDirty"/> is set, so renames and reorders are cheap.
    /// </summary>
    public async Task SaveGroupAsync(Group group, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        await BuildIconsIfDirtyAsync(group, ct);
        await _repository.SaveAsync(group, ct);
    }

    /// <summary>
    /// Rebuilds the group icon, creates a .lnk launcher shortcut, and attempts to
    /// pin it to the Windows taskbar. The caller (UI) is responsible for any
    /// follow-up such as opening the shortcut folder in Explorer.
    /// </summary>
    public async Task<PinResult> PinGroupToTaskbarAsync(Guid groupId, CancellationToken ct = default)
    {
        var group = await _repository.GetByIdAsync(groupId, ct)
            ?? throw new InvalidOperationException($"Group {groupId} not found.");

        try
        {
            await BuildIconsIfDirtyAsync(group, ct);
            await _repository.SaveAsync(group, ct);

            var launcherLinkPath = _shortcutService.CreateGroupLauncherLink(group, group.IconPath!);
            var pinned = _shortcutService.PinToTaskbar(group, launcherLinkPath);

            var shortcutDir = Path.GetDirectoryName(launcherLinkPath);
            if (shortcutDir is not null)
                _shell.RevealInFileManager(shortcutDir);

            return new PinResult(
                pinned ? PinOutcome.Pinned : PinOutcome.ShortcutCreatedManualPinRequired,
                launcherLinkPath);
        }
        catch (Exception ex)
        {
            return new PinResult(PinOutcome.Failed, string.Empty, ex.Message);
        }
    }

    private async Task BuildIconsIfDirtyAsync(Group group, CancellationToken ct)
    {
        var groupPath = _paths.GetGroupPath(group.Id);

        // Per-shortcut icons. Always run the extraction pipeline so a stale
        // 244-byte fallback PNG (left behind by an earlier broken build)
        // gets replaced with a real icon. GetIconPathAsync itself caches
        // and short-circuits when a real icon PNG already exists.
        foreach (var shortcut in group.Shortcuts)
        {
            shortcut.IconPath = await _iconCache.GetIconPathAsync(shortcut, groupPath, ct);
        }

        if (!group.IconCacheDirty && group.IconPath is not null && File.Exists(group.IconPath))
            return;

        group.IconPath = await _iconCache.BuildGroupIconAsync(group, groupPath, ct);
        group.MarkIconCacheClean();
    }
}
