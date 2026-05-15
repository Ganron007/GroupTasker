using GroupTasker.Domain.Interfaces;

namespace GroupTasker.Infrastructure.Configuration;

/// <summary>
/// Default <see cref="IConfigPathProvider"/>: anchors config under the executable's directory.
/// </summary>
public sealed class ConfigPathProvider : IConfigPathProvider
{
    public string ConfigRoot { get; }
    public string ShortcutFolder { get; }

    public ConfigPathProvider(string exeDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(exeDirectory);
        ConfigRoot = Path.Combine(exeDirectory, "config");
        ShortcutFolder = Path.Combine(exeDirectory, "shortcut");
    }

    public string GetGroupPath(Guid groupId) =>
        Path.Combine(ConfigRoot, "groups", groupId.ToString("N"));
}
