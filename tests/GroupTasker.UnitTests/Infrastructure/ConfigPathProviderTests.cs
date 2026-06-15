using System.IO;
using GroupTasker.Infrastructure.Configuration;

namespace GroupTasker.UnitTests.Infrastructure;

public class ConfigPathProviderTests : IDisposable
{
    private readonly string _tempExeDir = Path.Combine(Path.GetTempPath(), "gt-exe-" + Guid.NewGuid().ToString("N"));

    public ConfigPathProviderTests()
    {
        Directory.CreateDirectory(_tempExeDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempExeDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void PortableMode_WhenConfigDirExistsNextToExe()
    {
        Directory.CreateDirectory(Path.Combine(_tempExeDir, "config"));

        var provider = new ConfigPathProvider(_tempExeDir, appName: "GroupTasker-Test");

        Assert.Equal(Path.Combine(_tempExeDir, "config"), provider.ConfigRoot);
        Assert.Equal(Path.Combine(_tempExeDir, "shortcut"), provider.ShortcutFolder);
    }

    [Fact]
    public void InstalledMode_WhenNoConfigDir_FallsBackToLocalAppData()
    {
        var provider = new ConfigPathProvider(_tempExeDir, appName: "GroupTasker-Test");

        var expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GroupTasker-Test");

        Assert.Equal(Path.Combine(expectedRoot, "config"), provider.ConfigRoot);
        Assert.Equal(Path.Combine(expectedRoot, "shortcut"), provider.ShortcutFolder);
    }

    [Fact]
    public void GetGroupPath_ReturnsExpectedSubpath()
    {
        Directory.CreateDirectory(Path.Combine(_tempExeDir, "config"));
        var provider = new ConfigPathProvider(_tempExeDir);
        var groupId = Guid.Parse("12345678-1234-1234-1234-123456789abc");

        var path = provider.GetGroupPath(groupId);

        Assert.Equal(Path.Combine(_tempExeDir, "config", "groups", "12345678123412341234123456789abc"), path);
    }

    [Fact]
    public void InstalledMode_DoesNotCreateDirectories()
    {
        var provider = new ConfigPathProvider(_tempExeDir, appName: "GroupTasker-Test");

        var expectedConfig = provider.ConfigRoot;
        Assert.False(Directory.Exists(expectedConfig), "ConfigPathProvider should not create directories in installed mode");
    }
}
