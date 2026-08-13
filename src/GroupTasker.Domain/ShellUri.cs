namespace GroupTasker.Domain;

/// <summary>
/// URI scheme detection shared by resolution and dead-target checks.
/// Recognises protocol URIs like <c>steam://rungameid/730</c>,
/// <c>ms-settings:bluetooth</c> and <c>mailto:x@y</c>, while rejecting
/// Windows drive paths ("C:\…", colon at index 1) and UNC paths.
/// </summary>
public static class ShellUri
{
    /// <summary>
    /// True when <paramref name="value"/> looks like a URI with a real scheme
    /// (RFC 3986: ALPHA followed by ALPHA / DIGIT / + / - / .), not a drive
    /// letter path or UNC path.
    /// </summary>
    public static bool LooksLikeUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var colon = value.IndexOf(':');
        if (colon <= 1) return false; // no colon, or a drive letter ("C:")

        if (value.StartsWith(@"\\", StringComparison.Ordinal)) return false; // UNC

        for (var i = 0; i < colon; i++)
        {
            var c = value[i];
            if (!(char.IsLetterOrDigit(c) || c is '+' or '-' or '.'))
                return false;
        }

        return true;
    }
}
