using GroupTasker.Domain.Interfaces;

namespace GroupTasker.Infrastructure.Configuration;

/// <summary>
/// Default <see cref="IConfigPathProvider"/>. Uses portable mode (config next to the
/// executable) when a <c>config</c> folder already exists there — preserving backward
/// compatibility for existing installs. Otherwise anchors under
/// <c>%LocalAppData%\{appName}</c> so that installs under <c>C:\Program Files</c>
/// and MSIX packages work without write-permission issues.
/// </summary>
public sealed class ConfigPathProvider : IConfigPathProvider
{
    public string ConfigRoot { get; }
    public string ShortcutFolder { get; }

    public ConfigPathProvider(string exeDirectory, string appName = "GroupTasker")
    {
        ArgumentException.ThrowIfNullOrEmpty(exeDirectory);

        var portableConfig = Path.Combine(exeDirectory, "config");

        if (Directory.Exists(portableConfig))
        {
            ConfigRoot = portableConfig;
            ShortcutFolder = Path.Combine(exeDirectory, "shortcut");
        }
        else
        {
            var appRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                appName);
            ConfigRoot = Path.Combine(appRoot, "config");
            ShortcutFolder = Path.Combine(appRoot, "shortcut");
        }
    }

    public string GetGroupPath(Guid groupId) =>
        Path.Combine(ConfigRoot, "groups", groupId.ToString("N"));
}
