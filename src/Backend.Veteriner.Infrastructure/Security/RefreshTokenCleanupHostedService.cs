using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Security;

public sealed class RefreshTokenCleanupHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RefreshTokenCleanupHostedService> _logger;
    private readonly RefreshTokenCleanupOptions _opt;

    public RefreshTokenCleanupHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<RefreshTokenCleanupOptions> options,
        ILogger<RefreshTokenCleanupHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _opt = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opt.Enabled)
        {
            _logger.LogInformation("RefreshToken cleanup disabled.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _opt.IntervalMinutes));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnce(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RefreshToken cleanup failed.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // shutdown
            }
        }
    }

    private async Task RunOnce(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(-Math.Max(0, _opt.RetentionDays));
        var batchSize = Math.Max(100, _opt.BatchSize);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Silme kriteri:
        // - Expire olmuş tokenlar ve retention süresini aşmış olanlar
        // - veya revoke edilmiş ve retention süresini aşmış olanlar
        var deletedTotal = 0;

        while (!ct.IsCancellationRequested)
        {
            // Önce aday id’leri çek (batch)
            var ids = await db.RefreshTokens
                .Where(rt =>
                    (rt.ExpiresAtUtc < now && rt.CreatedAtUtc < cutoff) ||
                    (rt.RevokedAtUtc != null && rt.RevokedAtUtc < cutoff))
                .OrderBy(rt => rt.CreatedAtUtc)
                .Select(rt => rt.Id)
                .Take(batchSize)
                .ToListAsync(ct);

            if (ids.Count == 0) break;

            // EF Core 7/8+: ExecuteDelete ile hızlı silme
            var affected = await db.RefreshTokens
                .Where(rt => ids.Contains(rt.Id))
                .ExecuteDeleteAsync(ct);

            deletedTotal += affected;

            // Büyük tabloda uzun süre kilit tutmamak için bir nefes
            await Task.Delay(TimeSpan.FromMilliseconds(50), ct);
        }

        if (deletedTotal > 0)
            _logger.LogInformation("RefreshToken cleanup deleted {Count} rows. RetentionDays={RetentionDays}", deletedTotal, _opt.RetentionDays);
        else
            _logger.LogDebug("RefreshToken cleanup: nothing to delete.");
    }
}
