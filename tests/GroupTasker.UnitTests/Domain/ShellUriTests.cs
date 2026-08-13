using GroupTasker.Domain;

namespace GroupTasker.UnitTests.Domain;

public class ShellUriTests
{
    [Theory]
    [InlineData("steam://rungameid/730", true)]
    [InlineData("ms-settings:bluetooth", true)]
    [InlineData("mailto:someone@example.com", true)]
    [InlineData("com.epicgames.launcher://apps/x?action=launch", true)]
    [InlineData("shell:AppsFolder", true)]
    [InlineData("shell:AppsFolder\\Foo!Bar", true)]
    [InlineData(@"C:\Games\game.exe", false)]
    [InlineData(@"C:relative\path", false)]
    [InlineData(@"\\server\share\file.exe", false)]
    [InlineData("game.exe", false)]
    [InlineData("", false)]
    [InlineData("Foo!Bar", false)]
    [InlineData("C:", false)]
    public void LooksLikeUri_DetectsUriSchemes(string input, bool expected)
    {
        Assert.Equal(expected, ShellUri.LooksLikeUri(input));
    }
}
