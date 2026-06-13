using GroupTasker.Domain.Entities;

namespace GroupTasker.Domain.Interfaces;

/// <summary>
/// Activates a Windows app by its AppUserModelId (AUMI). Works for Store apps,
/// UWP apps, and any app that registers an AUMI in the Windows shell — survives
/// version changes because the AUMI is the stable identifier, not the .exe path.
/// </summary>
public interface IAppActivator
{
    /// <summary>Launch the app by AUMI. Returns true if activation was dispatched.</summary>
    bool ActivateByAumi(string appUserModelId);

    /// <summary>Try to extract the AUMI from a taskbar .lnk file.</summary>
    string? TryGetAumiFromLink(string lnkPath);
}
