using GroupTasker.Domain;

namespace GroupTasker.UnitTests.Domain;

public class FileNameSanitizerTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("with spaces", "with spaces")]
    [InlineData("a:b*c?d", "abcd")]
    [InlineData("foo/bar\\baz", "foobarbaz")]
    public void Sanitize_StripsInvalidChars(string input, string expected)
    {
        Assert.Equal(expected, FileNameSanitizer.Sanitize(input));
    }

    [Fact]
    public void Sanitize_EmptyOrNull_ReturnsUnderscore()
    {
        Assert.Equal("_", FileNameSanitizer.Sanitize(""));
    }
}
