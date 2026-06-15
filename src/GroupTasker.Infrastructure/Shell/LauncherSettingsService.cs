using System.Text.Json;
using GroupTasker.Domain.Interfaces;
using GroupTasker.Domain.Logging;
using GroupTasker.Infrastructure.Configuration;

namespace GroupTasker.Infrastructure.Shell;

public sealed class LauncherSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new HotkeyBindingJsonConverter() }
    };

    private readonly string _filePath;
    private readonly ILogger _logger;
    private readonly object _writeLock = new();

    public LauncherSettingsService(IConfigPathProvider paths, ILogger? logger = null)
    {
        _filePath = Path.Combine(paths.ConfigRoot, "launcher.json");
        _logger = logger ?? NullLogger.Instance;
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
            _logger.Error(ex, "Failed to read launcher settings from {FilePath}", _filePath);
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
            _logger.Error(ex, "Failed to save launcher settings to {FilePath}", _filePath);
        }
    }
}
