using GroupTasker.Domain.Entities;

namespace GroupTasker.UnitTests.Domain;

public class HotkeyBindingTests
{
    [Fact]
    public void Default_IsCtrlAltG()
    {
        var hk = HotkeyBinding.Default;
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, hk.Modifiers);
        Assert.Equal(0x47, hk.Key); // 'G'
    }

    [Theory]
    [InlineData(HotkeyModifiers.Control, 0x47, "Ctrl+G")]
    [InlineData(HotkeyModifiers.Alt, 0x47, "Alt+G")]
    [InlineData(HotkeyModifiers.Shift, 0x47, "Shift+G")]
    [InlineData(HotkeyModifiers.Win, 0x47, "Win+G")]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x47, "Ctrl+Alt+G")]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x47, "Ctrl+Alt+Shift+G")]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Win, 0x47, "Ctrl+Alt+Shift+Win+G")]
    public void ToString_ProducesCanonicalForm(HotkeyModifiers mods, int key, string expected)
    {
        Assert.Equal(expected, new HotkeyBinding(mods, key).ToString());
    }

    [Theory]
    [InlineData("Ctrl+G", HotkeyModifiers.Control, 0x47)]
    [InlineData("ctrl+g", HotkeyModifiers.Control, 0x47)]
    [InlineData("Control+G", HotkeyModifiers.Control, 0x47)]
    [InlineData("Alt+G", HotkeyModifiers.Alt, 0x47)]
    [InlineData("Shift+G", HotkeyModifiers.Shift, 0x47)]
    [InlineData("Win+G", HotkeyModifiers.Win, 0x47)]
    [InlineData("Meta+G", HotkeyModifiers.Win, 0x47)]
    [InlineData("Cmd+G", HotkeyModifiers.Win, 0x47)]
    [InlineData("Ctrl+Alt+G", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x47)]
    [InlineData("Alt+F4", HotkeyModifiers.Alt, 0x73)]
    [InlineData("Ctrl+Shift+F12", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x7B)]
    [InlineData("Ctrl+Space", HotkeyModifiers.Control, 0x20)]
    public void TryParse_ValidInput_ReturnsExpectedBinding(string text, HotkeyModifiers expectedMods, int expectedKey)
    {
        Assert.True(HotkeyBinding.TryParse(text, out var binding));
        Assert.NotNull(binding);
        Assert.Equal(expectedMods, binding!.Modifiers);
        Assert.Equal(expectedKey, binding.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("G")] // no modifier
    [InlineData("Ctrl")] // no key
    [InlineData("Ctrl+Plus")] // Plus isn't a valid key
    [InlineData("Ctrl+G+Q")] // two keys
    [InlineData("Ctrl+VK200")] // unknown VK code
    public void TryParse_InvalidInput_ReturnsFalse(string text)
    {
        Assert.False(HotkeyBinding.TryParse(text, out var binding));
        Assert.Null(binding);
    }

    [Fact]
    public void TryParse_Null_ReturnsFalse()
    {
        Assert.False(HotkeyBinding.TryParse(null, out var binding));
        Assert.Null(binding);
    }

    [Fact]
    public void Equality_BasedOnModifiersAndKey()
    {
        var a = new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x47);
        var b = new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x47);
        var c = new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x48);
        var d = new HotkeyBinding(HotkeyModifiers.Control, 0x47);

        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.False(a.Equals(c));
        Assert.False(a.Equals(d));
        Assert.False(a.Equals(null));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveKey()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HotkeyBinding(HotkeyModifiers.Control, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HotkeyBinding(HotkeyModifiers.Control, -1));
    }

    [Fact]
    public void ToString_Parse_RoundTrip()
    {
        foreach (var mods in new[]
        {
            HotkeyModifiers.Control,
            HotkeyModifiers.Alt,
            HotkeyModifiers.Shift,
            HotkeyModifiers.Win,
            HotkeyModifiers.Control | HotkeyModifiers.Alt,
            HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift,
        })
        {
            var original = new HotkeyBinding(mods, 0x47);
            var text = original.ToString();
            Assert.True(HotkeyBinding.TryParse(text, out var roundTripped), $"Failed to parse '{text}'");
            Assert.Equal(original, roundTripped);
        }
    }
}
