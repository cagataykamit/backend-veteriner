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
/// Bağlantı açılış süresini ölçer. Yavaş açılış genelde pool bekleme veya ağ/instans baskısı ile ilişkilidir;
/// komut yürütme süresinden ayrıştırılmak için <see cref="SlowQueryLoggingInterceptor"/> ile birlikte okunmalıdır.
/// </summary>
public sealed class DbConnectionSlowOpenInterceptor : DbConnectionInterceptor
{
    private readonly IOptions<PerformanceDiagnosticsOptions> _options;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<DbConnectionSlowOpenInterceptor> _logger;

    public DbConnectionSlowOpenInterceptor(
        IOptions<PerformanceDiagnosticsOptions> options,
        IHttpContextAccessor httpContextAccessor,
        ILogger<DbConnectionSlowOpenInterceptor> logger)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        LogIfSlow(eventData);
        base.ConnectionOpened(connection, eventData);
    }

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(eventData);
        return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private void LogIfSlow(ConnectionEndEventData eventData)
    {
        var opts = _options.Value;
        if (!opts.Enabled)
            return;

        var elapsedMs = (long)eventData.Duration.TotalMilliseconds;
        if (elapsedMs < opts.SlowConnectionOpenMs)
            return;

        var (path, correlationId) = ResolveHttpContext();
        _logger.LogWarning(
            "EF.SlowConnectionOpen DurationMs={DurationMs} ThresholdMs={ThresholdMs} ActiveHttpRequestsApprox={ActiveHttpRequestsApprox} Path={Path} CorrelationId={CorrelationId}",
            elapsedMs,
            opts.SlowConnectionOpenMs,
            ActiveHttpRequestCounter.ApproximateActive,
            path,
            correlationId);
    }

    private (string Path, string? CorrelationId) ResolveHttpContext()
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx is null)
            return ("(none)", null);

        var path = ctx.Request.Path.HasValue ? ctx.Request.Path.Value! : "/";
        ctx.Items.TryGetValue(Correlation.HeaderName, out var cidObj);
        var correlationId = cidObj?.ToString();
        return (path, correlationId);
    }
}
