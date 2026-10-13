namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>Beklenmeyen hataları sağlayıcıdan bağımsız raporlama noktası (log, Sentry, App Insights vb. uygulamaları bağlanır).</summary>
public interface IErrorReporter
{
    void Report(Exception exception, ErrorReportContext context);
}

public sealed record ErrorReportContext(string TraceId, string? CorrelationId, int StatusCode);
