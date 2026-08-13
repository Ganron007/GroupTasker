using GroupTasker.Domain.Entities;
using GroupTasker.Domain.Logging;
using GroupTasker.Infrastructure.IconExtraction;
using GroupTasker.Infrastructure.Shell;
using GroupTasker.UnitTests.Application;

namespace GroupTasker.UnitTests.Infrastructure;

public class WindowsShortcutServiceTests
{
    private readonly WindowsShortcutService _svc;

    public WindowsShortcutServiceTests()
    {
        _svc = new WindowsShortcutService(
            new IconExtractor(),
            new FakeConfigPathProvider(Path.Combine(Path.GetTempPath(), $"gt-test-{Guid.NewGuid():N}")),
            @"C:\app\GroupTasker.exe",
            logger: NullLogger.Instance);
    }

    [Fact]
    public void Resolve_UrlShortcut_IsLink()
    {
        var s = _svc.Resolve(@"C:\shortcuts\Game.url");

        Assert.Equal(ShortcutType.Link, s.Type);
        Assert.Equal(@"C:\shortcuts\Game.url", s.TargetPath);
        Assert.Equal("Game", s.DisplayName);
    }

    [Fact]
    public void Resolve_ClickOnceShortcut_IsLink()
    {
        var s = _svc.Resolve(@"C:\shortcuts\MyApp.appref-ms");

        Assert.Equal(ShortcutType.Link, s.Type);
        Assert.Equal(@"C:\shortcuts\MyApp.appref-ms", s.TargetPath);
    }

    [Fact]
    public void Resolve_PinnedSiteShortcut_IsLink()
    {
        var s = _svc.Resolve(@"C:\shortcuts\Docs.website");

        Assert.Equal(ShortcutType.Link, s.Type);
        Assert.Equal(@"C:\shortcuts\Docs.website", s.TargetPath);
    }

    [Theory]
    [InlineData("run.bat")]
    [InlineData("run.cmd")]
    [InlineData("run.ps1")]
    [InlineData("run.vbs")]
    [InlineData("services.msc")]
    public void Resolve_Scripts_AreLinks(string file)
    {
        var s = _svc.Resolve($@"C:\shortcuts\{file}");

        Assert.Equal(ShortcutType.Link, s.Type);
        Assert.Equal($@"C:\shortcuts\{file}", s.TargetPath);
    }

    [Fact]
    public void Resolve_ProtocolUri_IsLinkWithRawTarget()
    {
        var s = _svc.Resolve("steam://rungameid/730");

        Assert.Equal(ShortcutType.Link, s.Type);
        Assert.Equal("steam://rungameid/730", s.TargetPath);
    }

    [Fact]
    public void Resolve_StoreAppIdWithShellPrefix_StripsPrefix()
    {
        var s = _svc.Resolve(@"shell:AppsFolder\Publisher.App!Game");

        Assert.Equal(ShortcutType.StoreApp, s.Type);
        Assert.Equal("Publisher.App!Game", s.TargetPath);
    }
}
