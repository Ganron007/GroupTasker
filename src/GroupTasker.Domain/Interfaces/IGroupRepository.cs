using GroupTasker.Domain.Entities;

namespace GroupTasker.Domain.Interfaces;

/// <summary>
/// Persistence contract for groups. Implementations write to JSON, database, etc.
/// </summary>
public interface IGroupRepository
{
    /// <summary>Load all groups from storage.</summary>
    Task<IReadOnlyList<Group>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Load a single group by ID.</summary>
    Task<Group?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Save (create or update) a group.</summary>
    Task SaveAsync(Group group, CancellationToken ct = default);

    /// <summary>Delete a group by ID.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
