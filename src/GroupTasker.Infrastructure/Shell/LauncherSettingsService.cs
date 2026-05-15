using System.Text.Json;
using GroupTasker.Domain.Interfaces;
using GroupTasker.Infrastructure.Configuration;

namespace GroupTasker.Infrastructure.Shell;

public sealed class LauncherSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _filePath;
    private readonly Action<string, Exception>? _onError;
    private readonly object _writeLock = new();

    public LauncherSettingsService(IConfigPathProvider paths, Action<string, Exception>? onError = null)
    {
        _filePath = Path.Combine(paths.ConfigRoot, "launcher.json");
        _onError = onError;
    }

    public string FilePath => _filePath;

    public LauncherSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<LauncherSettings>(json, JsonOptions)
                       ?? new LauncherSettings();
            }
        }
        catch (Exception ex)
        {
            _onError?.Invoke($"Failed to read launcher settings from {_filePath}", ex);
        }

        return new LauncherSettings();
    }

    public void Save(LauncherSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir is not null)
                Directory.CreateDirectory(dir);

            // Atomic write so a crash mid-save can't corrupt launcher.json.
            lock (_writeLock)
            {
                var tmp = _filePath + ".tmp";
                var json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(tmp, json);
                File.Move(tmp, _filePath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            _onError?.Invoke($"Failed to save launcher settings to {_filePath}", ex);
        }
    }
}
