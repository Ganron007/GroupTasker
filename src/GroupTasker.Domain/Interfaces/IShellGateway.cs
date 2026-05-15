namespace GroupTasker.Domain.Interfaces;

/// <summary>
/// Narrow seam for OS-level shell actions the application layer occasionally needs.
/// Keeping these behind an interface stops <c>System.Diagnostics.Process</c> calls
/// leaking into the Application project.
/// </summary>
public interface IShellGateway
{
    /// <summary>Open the given folder in the platform's file manager (Explorer on Windows).</summary>
    void RevealInFileManager(string folderPath);
}
