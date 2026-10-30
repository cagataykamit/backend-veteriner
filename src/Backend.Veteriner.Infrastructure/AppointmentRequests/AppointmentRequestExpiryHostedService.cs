using Backend.Veteriner.Application.AppointmentRequests;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.AppointmentRequests;

public sealed class AppointmentRequestExpiryOptions
{
    public bool Enabled { get; init; } = true;
    public int InitialDelaySeconds { get; init; } = 120;
    public int IntervalMinutes { get; init; } = 15;
    public int BatchSize { get; init; } = 200;
}

/// <summary>
/// İstenen saati geçmiş bekleyen talepleri "süresi doldu" yapar (ADR-042 K4). Kiracılar arası tarama sistem kapsamında; her talep
/// kendi geçişini geçmişe yazar. Sahibe e-posta gönderilmez (ADR K5 listesinde yok).
/// </summary>
public sealed class AppointmentRequestExpiryHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AppointmentRequestExpiryHostedService> _logger;
    private readonly AppointmentRequestExpiryOptions _options;
    private readonly TimeProvider _timeProvider;

    public AppointmentRequestExpiryHostedService(
        IServiceScopeFactory scopes,
        IOptions<AppointmentRequestExpiryOptions> options,
        ILogger<AppointmentRequestExpiryHostedService> logger,
        TimeProvider timeProvider)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
            return;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, _options.InitialDelaySeconds)), stoppingToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var expired = await RunOnceAsync(stoppingToken);
                if (expired > 0)
                    _logger.LogInformation("Süresi dolan {Count} randevu talebi kapatıldı.", expired);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Randevu talebi süre taraması başarısız.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // kapanış
            }
        }
    }

    /// <summary>Bir tarama turu; kapatılan talep sayısı.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var total = 0;

        while (!ct.IsCancellationRequested)
        {
            var spec = new DueAppointmentRequestsSpec(utcNow, Math.Max(1, _options.BatchSize));
            var due = await Ardalis.Specification.EntityFrameworkCore.SpecificationEvaluator.Default
                .GetQuery(db.AppointmentRequests, spec)
                .ToListAsync(ct);
            if (due.Count == 0)
                break;

            total += due.Count(r => r.ExpireIfDue(utcNow));
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Aynı anda karar verilen talep: bir sonraki turda yeniden değerlendirilir.
                db.ChangeTracker.Clear();
                break;
            }

            db.ChangeTracker.Clear();
        }

        return total;
    }
}
