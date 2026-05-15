namespace GroupTasker.Domain;

/// <summary>
/// Single source of truth for file-name sanitisation. Replaces the two near-identical
/// implementations that used to live in <c>WindowsShortcutService</c> and <c>IconCacheService</c>.
/// </summary>
public static class FileNameSanitizer
{
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    public static string Sanitize(string name)
    {
        if (string.IsNullOrEmpty(name)) return "_";
        return string.Concat(name.Where(c => Array.IndexOf(InvalidChars, c) < 0));
    }
}
