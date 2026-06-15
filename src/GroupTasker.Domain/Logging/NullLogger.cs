using GroupTasker.Domain.Interfaces;

namespace GroupTasker.Domain.Logging;

/// <summary>
/// No-op logger for tests and environments where logging is not configured.
/// </summary>
public sealed class NullLogger : ILogger
{
    public static ILogger Instance { get; } = new NullLogger();

    public void Debug(string messageTemplate, params object?[] propertyValues) { }
    public void Information(string messageTemplate, params object?[] propertyValues) { }
    public void Warning(string messageTemplate, params object?[] propertyValues) { }
    public void Warning(Exception exception, string messageTemplate, params object?[] propertyValues) { }
    public void Error(string messageTemplate, params object?[] propertyValues) { }
    public void Error(Exception exception, string messageTemplate, params object?[] propertyValues) { }
}
