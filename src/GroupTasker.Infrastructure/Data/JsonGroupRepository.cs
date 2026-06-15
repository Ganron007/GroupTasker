using System.Text.Json;
using System.Text.Json.Serialization;
using GroupTasker.Domain.Entities;
using GroupTasker.Domain.Interfaces;
using GroupTasker.Domain.Logging;

namespace GroupTasker.Infrastructure.Data;

/// <summary>
/// JSON file-based persistence for groups. Each group is stored as a separate
/// JSON file under <c>{ConfigRoot}/groups/{id}/group.json</c>.
/// Writes are atomic (tmp file + <see cref="File.Move(string,string,bool)"/>) and
/// per-group writes are serialised by a key lock so concurrent saves can't corrupt the file.
/// </summary>
public sealed class JsonGroupRepository : IGroupRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Write enums as string names going forward (so a future ordinal reshuffle
        // can't silently re-classify existing entries). Reads still accept the legacy
        // integer form via JsonStringEnumConverter's default allowIntegerValues=true,
        // so existing group.json files continue to deserialise correctly.
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IConfigPathProvider _paths;
    private readonly ILogger _logger;
    private readonly Dictionary<Guid, SemaphoreSlim> _writeLocks = new();
    private readonly object _writeLocksGate = new();

    public JsonGroupRepository(IConfigPathProvider paths, ILogger? logger = null)
    {
        _paths = paths;
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task<IReadOnlyList<Group>> GetAllAsync(CancellationToken ct = default)
    {
        var groupsDir = Path.Combine(_paths.ConfigRoot, "groups");
        if (!Directory.Exists(groupsDir))
            return Array.Empty<Group>();

        var groups = new List<Group>();
        foreach (var dir in Directory.GetDirectories(groupsDir))
        {
            ct.ThrowIfCancellationRequested();
            var file = Path.Combine(dir, "group.json");
            if (!File.Exists(file)) continue;

            var group = await ReadGroupAsync(file, ct).ConfigureAwait(false);
            if (group is not null) groups.Add(group);
        }

        return groups.OrderBy(g => g.CreatedAt).ToList();
    }

    public Task<Group?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => ReadGroupAsync(GetGroupFilePath(id), ct);

    public async Task SaveAsync(Group group, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        // ModifiedAt is the domain's responsibility (Group.MarkModified). The repository
        // must NOT bump timestamps — that used to make a round-trip non-idempotent.

        var dir = GetGroupDir(group.Id);
        Directory.CreateDirectory(dir);
        var finalPath = Path.Combine(dir, "group.json");

        var sem = GetLockFor(group.Id);
        await sem.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tmpPath = finalPath + ".tmp";
            var json = JsonSerializer.Serialize(group, JsonOptions);
            await File.WriteAllTextAsync(tmpPath, json, ct).ConfigureAwait(false);
            // Atomic swap so partial writes can never leave a half-written group.json behind.
            File.Move(tmpPath, finalPath, overwrite: true);
        }
        finally
        {
            sem.Release();
        }
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var dir = GetGroupDir(id);
        if (!Directory.Exists(dir)) return Task.FromResult(false);

        try
        {
            Directory.Delete(dir, recursive: true);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete group {GroupId}", id);
            return Task.FromResult(false);
        }
    }

    private async Task<Group?> ReadGroupAsync(string file, CancellationToken ct)
    {
        if (!File.Exists(file)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<Group>(json, JsonOptions);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to read group from {FilePath}", file);
            return null;
        }
    }

    private SemaphoreSlim GetLockFor(Guid id)
    {
        lock (_writeLocksGate)
        {
            if (!_writeLocks.TryGetValue(id, out var sem))
            {
                sem = new SemaphoreSlim(1, 1);
                _writeLocks[id] = sem;
            }
            return sem;
        }
    }

    private string GetGroupDir(Guid id) => _paths.GetGroupPath(id);
    private string GetGroupFilePath(Guid id) => Path.Combine(GetGroupDir(id), "group.json");
}
