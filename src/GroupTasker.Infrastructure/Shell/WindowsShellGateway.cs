using System.Diagnostics;
using GroupTasker.Domain.Interfaces;

namespace GroupTasker.Infrastructure.Shell;

/// <summary>Windows implementation of <see cref="IShellGateway"/>: launches Explorer.</summary>
public sealed class WindowsShellGateway : IShellGateway
{
    public void RevealInFileManager(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        if (!Directory.Exists(folderPath)) return;

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folderPath}\"",
            UseShellExecute = true
        });
    }
}
