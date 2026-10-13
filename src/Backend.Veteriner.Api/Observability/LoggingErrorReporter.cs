using Backend.Veteriner.Application.Common.Abstractions;

namespace Backend.Veteriner.Api.Observability;

/// <summary>Varsayılan <see cref="IErrorReporter"/>: yapılandırılmış günlük + sayaç. Dış hizmete veri göndermez.</summary>
public sealed class LoggingErrorReporter : IErrorReporter
{
    private readonly ILogger<LoggingErrorReporter> _logger;
    private readonly OperationsMetrics _metrics;

    public LoggingErrorReporter(ILogger<LoggingErrorReporter> logger, OperationsMetrics metrics)
    {
        _logger = logger;
        _metrics = metrics;
    }

    public void Report(Exception exception, ErrorReportContext context)
    {
        _metrics.RecordUnhandledError(exception);
        _logger.LogError(
            exception,
            "UnhandledException StatusCode={StatusCode} TraceId={TraceId} CorrelationId={CorrelationId}",
            context.StatusCode,
            context.TraceId,
            context.CorrelationId);
    }
}
