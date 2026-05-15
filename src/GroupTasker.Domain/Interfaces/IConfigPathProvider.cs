namespace GroupTasker.Domain.Interfaces;

/// <summary>
/// Resolves the on-disk locations the app reads/writes (config root, group folders).
/// Replaces the old practice of passing a raw <c>string configRoot</c> through every
/// constructor — that made tests painful and obscured the meaning of the parameter.
/// </summary>
public interface IConfigPathProvider
{
    /// <summary>Root config directory (e.g. <c>{exeDir}\config</c>).</summary>
    string ConfigRoot { get; }

    /// <summary>Folder containing this group's icon and JSON (<c>{ConfigRoot}\groups\{id-N}</c>).</summary>
    string GetGroupPath(Guid groupId);

    /// <summary>Folder where pinnable .lnk files are emitted (<c>{exeDir}\shortcut</c>).</summary>
    string ShortcutFolder { get; }
}
