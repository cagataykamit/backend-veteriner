using System.Data.Common;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Application.Common.Diagnostics;
using Backend.Veteriner.Application.Common.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Persistence;

/// <summary>
/// EF Core komut süresi eşiğini aşan sorguları loglar (<see cref="PerformanceDiagnosticsOptions.SlowSqlMs"/>).
/// </summary>
public sealed class SlowQueryLoggingInterceptor : DbCommandInterceptor
{
    private readonly IOptions<PerformanceDiagnosticsOptions> _options;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SlowQueryLoggingInterceptor> _logger;

    public SlowQueryLoggingInterceptor(
        IOptions<PerformanceDiagnosticsOptions> options,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SlowQueryLoggingInterceptor> logger)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        LogIfSlow(eventData, command.CommandText);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(eventData, command.CommandText);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        LogIfSlow(eventData, command.CommandText);
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(eventData, command.CommandText);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        LogIfSlow(eventData, command.CommandText);
        return base.ScalarExecuted(command, eventData, result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(eventData, command.CommandText);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    private void LogIfSlow(CommandExecutedEventData eventData, string? commandText)
    {
        var opts = _options.Value;
        if (!opts.Enabled)
            return;

        var elapsedMs = (long)eventData.Duration.TotalMilliseconds;
        if (elapsedMs < opts.SlowSqlMs)
            return;

        var preview = Truncate(commandText ?? string.Empty, opts.SqlPreviewMaxChars);
        var (path, correlationId) = ResolveHttpContext();
        _logger.LogWarning(
            "EF.SlowSql ElapsedMs={ElapsedMs} ThresholdMs={ThresholdMs} ActiveHttpRequestsApprox={ActiveHttpRequestsApprox} Path={Path} CorrelationId={CorrelationId} SqlPreview={SqlPreview}",
            elapsedMs,
            opts.SlowSqlMs,
            ActiveHttpRequestCounter.ApproximateActive,
            path,
            correlationId,
            preview);
    }

    private (string Path, string? CorrelationId) ResolveHttpContext()
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx is null)
            return ("(none)", null);

        var path = ctx.Request.Path.HasValue ? ctx.Request.Path.Value! : "/";
        ctx.Items.TryGetValue(Correlation.HeaderName, out var cidObj);
        return (path, cidObj?.ToString());
    }

    private static string Truncate(string text, int maxChars)
    {
        if (maxChars <= 0 || text.Length <= maxChars)
            return text;
        return text[..maxChars] + "…";
    }
}
