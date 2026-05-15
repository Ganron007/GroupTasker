using System.Collections.Immutable;
using GroupTasker.Domain.Entities;

namespace GroupTasker.Domain.Interfaces;

/// <summary>
/// Icon extraction and caching. Heavy lifting (COM interop, shell API) lives behind this interface.
/// </summary>
public interface IIconCacheService
{
    /// <summary>Extract or load the icon for a shortcut. Returns the relative cache path.</summary>
    Task<string> GetIconPathAsync(Shortcut shortcut, string groupPath, CancellationToken ct = default);

    /// <summary>Build the group icon (composite from shortcuts or custom image).</summary>
    Task<string> BuildGroupIconAsync(Group group, string groupPath, CancellationToken ct = default);

    /// <summary>Rebuild the entire icon cache for a group.</summary>
    Task RebuildCacheAsync(Group group, string groupPath, CancellationToken ct = default);

    /// <summary>
    /// Icon sizes we support (from largest to smallest). Immutable so consumers cannot
    /// accidentally mutate the static list (the old <c>static readonly int[]</c> exposed
    /// a mutable singleton).
    /// </summary>
    static readonly ImmutableArray<int> IconSizes = [256, 128, 64, 48, 32, 24, 16];
}
