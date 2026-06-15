using System.Diagnostics;
using GroupTasker.Domain.Logging;
using Serilog;
using Serilog.Events;

namespace GroupTasker.Infrastructure.Logging;

/// <summary>
/// Creates the production Serilog logger. Failures are swallowed and fall back
/// to a no-op logger so a logging problem can never prevent the app from starting.
/// </summary>
public static class SerilogBootstrap
{
    public static Domain.Interfaces.ILogger CreateLogger(string appName = "GroupTasker")
    {
        try
        {
            var logsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                appName,
                "logs");

            Directory.CreateDirectory(logsDir);

            var config = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.WithProperty("Application", appName)
                .WriteTo.Debug(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    Path.Combine(logsDir, "log-.txt"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Information);

            return new SerilogLogger(config.CreateLogger());
        }
        catch (Exception ex)
        {
            // Last-ditch: we cannot depend on the logger to log its own failure.
            Debug.WriteLine($"[GroupTasker] Failed to create Serilog logger: {ex}");
            return NullLogger.Instance;
        }
    }
}
