using Backend.Veteriner.Application.Ai.Drafts;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Ai;

public sealed class AiDraftExpiryOptions
{
    public const string SectionName = "Ai:DraftExpiry";

    public bool Enabled { get; init; } = true;
    public int InitialDelaySeconds { get; init; } = 180;
    public int IntervalMinutes { get; init; } = 60;
    public int BatchSize { get; init; } = 200;
}

/// <summary>
/// Süresi geçmiş AI taslaklarını "süresi doldu" yapar ve klinik içeriklerini siler (AI-009 saklama süresi,
/// <c>Ai:DraftLifetimeDays</c>). Karar alanları ve kaynak kimlikleri denetim için kalır. Kiracılar arası tarama sistem
/// kapsamında. Okuma anında süre zaten uygulanır (<c>AiDraft.StatusAt</c>); bu iş içeriği veritabanından temizler.
/// </summary>
public sealed class AiDraftExpiryHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AiDraftExpiryHostedService> _logger;
    private readonly AiDraftExpiryOptions _options;
    private readonly TimeProvider _timeProvider;

    public AiDraftExpiryHostedService(
        IServiceScopeFactory scopes,
        IOptions<AiDraftExpiryOptions> options,
        ILogger<AiDraftExpiryHostedService> logger,
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
                    _logger.LogInformation("Süresi dolan {Count} AI taslağı kapatıldı.", expired);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "AI taslağı süre taraması başarısız.");
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

    /// <summary>Bir tarama turu; kapatılan taslak sayısı.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var total = 0;

        while (!ct.IsCancellationRequested)
        {
            var spec = new DueAiDraftsSpec(utcNow, Math.Max(1, _options.BatchSize));
            var due = await Ardalis.Specification.EntityFrameworkCore.SpecificationEvaluator.Default
                .GetQuery(db.AiDrafts, spec)
                .ToListAsync(ct);
            if (due.Count == 0)
                break;

            total += due.Count(d => d.ExpireIfDue(utcNow));
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Aynı anda karar verilen taslak: bir sonraki turda yeniden değerlendirilir.
                db.ChangeTracker.Clear();
                break;
            }

            db.ChangeTracker.Clear();
        }

        return total;
    }
}
