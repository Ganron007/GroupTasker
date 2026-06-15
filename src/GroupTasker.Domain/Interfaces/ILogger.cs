namespace GroupTasker.Domain.Interfaces;

/// <summary>
/// Minimal logging seam used by the Domain and Application layers.
/// Implementations live in Infrastructure so the core has no external dependencies.
/// Message templates use Serilog-style named placeholders, e.g. <c>{GroupId}</c>.
/// </summary>
public interface ILogger
{
    void Debug(string messageTemplate, params object?[] propertyValues);
    void Information(string messageTemplate, params object?[] propertyValues);
    void Warning(string messageTemplate, params object?[] propertyValues);
    void Warning(Exception exception, string messageTemplate, params object?[] propertyValues);
    void Error(string messageTemplate, params object?[] propertyValues);
    void Error(Exception exception, string messageTemplate, params object?[] propertyValues);
}
