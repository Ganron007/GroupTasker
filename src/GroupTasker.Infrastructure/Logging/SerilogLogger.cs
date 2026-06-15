using GroupTasker.Domain.Interfaces;

namespace GroupTasker.Infrastructure.Logging;

/// <summary>
/// Domain <see cref="ILogger"/> implementation backed by Serilog.
/// </summary>
public sealed class SerilogLogger : ILogger
{
    private readonly Serilog.ILogger _logger;

    public SerilogLogger(Serilog.ILogger logger)
    {
        _logger = logger.ForContext<SerilogLogger>();
    }

    public void Debug(string messageTemplate, params object?[] propertyValues)
        => _logger.Debug(messageTemplate, propertyValues);

    public void Information(string messageTemplate, params object?[] propertyValues)
        => _logger.Information(messageTemplate, propertyValues);

    public void Warning(string messageTemplate, params object?[] propertyValues)
        => _logger.Warning(messageTemplate, propertyValues);

    public void Warning(Exception exception, string messageTemplate, params object?[] propertyValues)
        => _logger.Warning(exception, messageTemplate, propertyValues);

    public void Error(string messageTemplate, params object?[] propertyValues)
        => _logger.Error(messageTemplate, propertyValues);

    public void Error(Exception exception, string messageTemplate, params object?[] propertyValues)
        => _logger.Error(exception, messageTemplate, propertyValues);
}
