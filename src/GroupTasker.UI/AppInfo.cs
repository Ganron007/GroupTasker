using System.Reflection;

namespace GroupTasker.UI;

/// <summary>
/// Static metadata for the running build. Reads from the entry assembly so the
/// version stays in lockstep with <c>Directory.Build.props</c> — no string to drift.
/// </summary>
public static class AppInfo
{
    private static readonly Assembly EntryAssembly =
        Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

    /// <summary>The product display name (e.g. "GroupTasker").</summary>
    public static string ProductName =>
        EntryAssembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "GroupTasker";

    /// <summary>Semantic version, e.g. "1.1.0".</summary>
    public static string Version =>
        EntryAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? EntryAssembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    /// <summary>"v1.1.0" — for UI chips / tooltips.</summary>
    public static string VersionLabel => $"v{Version}";

    /// <summary>"GroupTasker v1.1.0" — for window titles.</summary>
    public static string TitleWithVersion => $"{ProductName} {VersionLabel}";
}
