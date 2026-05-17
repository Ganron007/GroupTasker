using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace GroupTasker.Launcher;

static class Program
{
    private const string RuntimeUrl = "https://dotnet.microsoft.com/download/dotnet/9.0";
    private const string AppExe = "GroupTasker.App.exe";

    [STAThread]
    static void Main(string[] args)
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var appPath = Path.Combine(appDir, AppExe);

        if (!File.Exists(appPath))
        {
            MessageBox.Show(
                $"Could not find {AppExe} in the application directory.\n\n" +
                "Please reinstall GroupTasker.",
                "GroupTasker",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        if (!IsDotNet9Installed())
        {
            var result = MessageBox.Show(
                "GroupTasker requires .NET 9 Runtime to run.\n\n" +
                "Click OK to download it from Microsoft.",
                ".NET Runtime Required",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);

            if (result == DialogResult.OK)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = RuntimeUrl,
                    UseShellExecute = true
                });
            }
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = appPath,
                WorkingDirectory = appDir,
            };

            if (args.Length > 0)
            {
                var quoted = new string[args.Length];
                for (int i = 0; i < args.Length; i++)
                    quoted[i] = "\"" + args[i].Replace("\"", "\"\"") + "\"";
                psi.Arguments = string.Join(" ", quoted);
            }

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to launch GroupTasker:\n{ex.Message}",
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static bool IsDotNet9Installed()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App");

            if (key is null)
            {
                // Fallback: try running 'dotnet --list-runtimes'
                return CheckDotNetViaProcess();
            }

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                if (subKeyName.StartsWith("9."))
                    return true;
            }

            return CheckDotNetViaProcess();
        }
        catch
        {
            return CheckDotNetViaProcess();
        }
    }

    private static bool CheckDotNetViaProcess()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--list-runtimes",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return false;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return output.Contains("Microsoft.NETCore.App 9.");
        }
        catch
        {
            return false;
        }
    }
}
