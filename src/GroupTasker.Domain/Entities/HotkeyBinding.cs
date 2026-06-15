namespace GroupTasker.Domain.Entities;

/// <summary>
/// A keyboard shortcut binding: a non-empty set of modifier keys plus one non-modifier key.
/// Serialised as a string in <c>launcher.json</c> using the canonical form
/// <c>Ctrl+Alt+Shift+Win+VkName</c> (e.g. <c>"Ctrl+Alt+G"</c>).
/// </summary>
public sealed class HotkeyBinding : IEquatable<HotkeyBinding>
{
    public HotkeyModifiers Modifiers { get; }
    public int Key { get; }

    public HotkeyBinding(HotkeyModifiers modifiers, int key)
    {
        if (key <= 0)
            throw new ArgumentOutOfRangeException(nameof(key), "Key must be a positive virtual-key code.");

        var nonMod = modifiers & ~HotkeyModifiers.Control & ~HotkeyModifiers.Alt & ~HotkeyModifiers.Shift & ~HotkeyModifiers.Win;
        if (nonMod != HotkeyModifiers.None)
            throw new ArgumentException("Non-modifier flag passed to Modifiers.", nameof(modifiers));

        Modifiers = modifiers;
        Key = key;
    }

    public static HotkeyBinding Default => new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x47); // 0x47 = 'G'

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt))     parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift))   parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win))     parts.Add("Win");
        parts.Add(VirtualKeyName.GetName(Key));
        return string.Join("+", parts);
    }

    public static bool TryParse(string? text, out HotkeyBinding? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false; // need at least one modifier + one key

        var mods = HotkeyModifiers.None;
        int? key = null;
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= HotkeyModifiers.Control; break;
                case "alt":                   mods |= HotkeyModifiers.Alt;     break;
                case "shift":                 mods |= HotkeyModifiers.Shift;   break;
                case "win": case "meta": case "cmd": mods |= HotkeyModifiers.Win; break;
                default:
                    if (key is not null) return false; // already have a key
                    if (!VirtualKeyName.TryGetCode(part, out var code)) return false;
                    key = code;
                    break;
            }
        }

        if (key is null) return false;
        if (mods == HotkeyModifiers.None) return false; // bare key without modifier would steal too many keystrokes

        try
        {
            result = new HotkeyBinding(mods, key.Value);
            return true;
        }
        catch
        {
            result = null;
            return false;
        }
    }

    public bool Equals(HotkeyBinding? other) =>
        other is not null && Modifiers == other.Modifiers && Key == other.Key;

    public override bool Equals(object? obj) => Equals(obj as HotkeyBinding);
    public override int GetHashCode() => HashCode.Combine((int)Modifiers, Key);
}

/// <summary>
/// Win32 modifier flags for <c>RegisterHotKey</c>. Values match the <c>MOD_*</c> constants in
/// <c>Winuser.h</c> so they can be cast straight through to the P/Invoke call.
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    None    = 0x0000,
    Alt     = 0x0001,
    Control = 0x0002,
    Shift   = 0x0004,
    Win     = 0x0008,
}

internal static class VirtualKeyName
{
    private static readonly Dictionary<string, int> _byName = new(StringComparer.OrdinalIgnoreCase)
    {
        // Letters
        ["A"] = 0x41, ["B"] = 0x42, ["C"] = 0x43, ["D"] = 0x44, ["E"] = 0x45,
        ["F"] = 0x46, ["G"] = 0x47, ["H"] = 0x48, ["I"] = 0x49, ["J"] = 0x4A,
        ["K"] = 0x4B, ["L"] = 0x4C, ["M"] = 0x4D, ["N"] = 0x4E, ["O"] = 0x4F,
        ["P"] = 0x50, ["Q"] = 0x51, ["R"] = 0x52, ["S"] = 0x53, ["T"] = 0x54,
        ["U"] = 0x55, ["V"] = 0x56, ["W"] = 0x57, ["X"] = 0x58, ["Y"] = 0x59,
        ["Z"] = 0x5A,
        // Digits
        ["0"] = 0x30, ["1"] = 0x31, ["2"] = 0x32, ["3"] = 0x33, ["4"] = 0x34,
        ["5"] = 0x35, ["6"] = 0x36, ["7"] = 0x37, ["8"] = 0x38, ["9"] = 0x39,
        // Function keys
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73, ["F5"] = 0x74,
        ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77, ["F9"] = 0x78, ["F10"] = 0x79,
        ["F11"] = 0x7A, ["F12"] = 0x7B, ["F13"] = 0x7C, ["F14"] = 0x7D, ["F15"] = 0x7E,
        ["F16"] = 0x7F, ["F17"] = 0x80, ["F18"] = 0x81, ["F19"] = 0x82, ["F20"] = 0x83,
        ["F21"] = 0x84, ["F22"] = 0x85, ["F23"] = 0x86, ["F24"] = 0x87,
        // Common special keys
        ["Space"]     = 0x20,
        ["Enter"]     = 0x0D,
        ["Escape"]    = 0x1B,
        ["Tab"]       = 0x09,
        ["Backspace"] = 0x08,
        ["Delete"]    = 0x2E,
        ["Insert"]    = 0x2D,
        ["Home"]      = 0x24,
        ["End"]       = 0x23,
        ["PageUp"]    = 0x21,
        ["PageDown"]  = 0x22,
        ["Left"]      = 0x25,
        ["Up"]        = 0x26,
        ["Right"]     = 0x27,
        ["Down"]      = 0x28,
    };

    public static string GetName(int vkCode) =>
        _byName.FirstOrDefault(kv => kv.Value == vkCode).Key ?? $"Vk{vkCode:X2}";

    public static bool TryGetCode(string name, out int code) => _byName.TryGetValue(name, out code);
}
