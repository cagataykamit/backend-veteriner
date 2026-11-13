using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.EDocuments;
using Backend.Veteriner.Application.EDocuments.Automation;
using Backend.Veteriner.Application.EDocuments.Specs;
using Backend.Veteriner.Domain.EDocuments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.EDocuments;

/// <summary>
/// Bekleyen e-belgelere (asenkron sonuç, geçici hata, süreç düşmesi, iptal) zamanı gelince bakar (ADR-036 ilke 4).
/// Belgeleri bulmak kiracılar arasıdır (<c>AsSystem</c>); her belge kendi kiracısının kapsamında ve ayrı DI kapsamında işlenir.
/// </summary>
public sealed class EDocumentStatusProcessor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EDocumentProcessorOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EDocumentStatusProcessor> _logger;

    public EDocumentStatusProcessor(
        IServiceScopeFactory scopeFactory,
        IOptions<EDocumentProcessorOptions> options,
        TimeProvider timeProvider,
        ILogger<EDocumentStatusProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <returns>İşlenen belge sayısı.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<EDocumentDueItem> due;
        using (var scope = _scopeFactory.CreateScope())
        {
            var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantScope>();
            var documents = scope.ServiceProvider.GetRequiredService<IReadRepository<EDocument>>();
            using (tenantScope.AsSystem())
                due = await documents.ListAsync(new EDocumentsDueForCheckSpec(_timeProvider.GetUtcNow().UtcDateTime, Math.Max(1, _options.BatchSize)), ct);
        }

        foreach (var item in due)
        {
            try
            {
                await ProcessAsync(item, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "e-belge kontrolü başarısız. EDocumentId={EDocumentId}", item.Id);
            }
        }

        return due.Count;
    }

    private async Task ProcessAsync(EDocumentDueItem item, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        using var tenant = services.GetRequiredService<ITenantScope>().UseTenant(item.TenantId);

        var documents = services.GetRequiredService<IRepository<EDocument>>();
        var connections = services.GetRequiredService<IRepository<EDocumentConnection>>();
        var sender = services.GetRequiredService<EDocumentSender>();
        var uow = services.GetRequiredService<IUnitOfWork>();

        var document = await documents.FirstOrDefaultAsync(new EDocumentByIdSpec(item.TenantId, item.Id, tracked: true), ct);
        if (document is null)
            return;
        var connection = await connections.FirstOrDefaultAsync(
            new EDocumentConnectionByClinicSpec(item.TenantId, document.ClinicId, tracked: true), ct);

        await sender.CheckAsync(document, connection, _options.MaxAttempts, ct);
        if (document.Status == EDocumentStatus.Failed
            && (EDocumentAutoIssueGuard.IsAccountWide(document.LastErrorKind) || connection is { Status: EDocumentConnectionStatus.ReauthorizationRequired }))
        {
            // Sonucu sonradan gelen belgede hesap genelinde kalıcı hata: otomatik kesme açıksa durur (yeni ücretler aynı hatayı almasın).
            await services.GetRequiredService<EDocumentAutoIssueGuard>().PauseAsync(item.TenantId, document.ClinicId, document.LastErrorMessage, ct);
        }

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Kullanıcı aynı anda belgeyi güncelledi (yenile / iptal); bir sonraki turda güncel hâline bakılır.
            _logger.LogInformation("e-belge eşzamanlı güncellendi; tur atlandı. EDocumentId={EDocumentId}", item.Id);
        }
    }
}

public sealed class EDocumentStatusHostedService : BackgroundService
{
    private readonly EDocumentStatusProcessor _processor;
    private readonly EDocumentProcessorOptions _options;
    private readonly ILogger<EDocumentStatusHostedService> _logger;

    public EDocumentStatusHostedService(
        EDocumentStatusProcessor processor,
        IOptions<EDocumentProcessorOptions> options,
        ILogger<EDocumentStatusHostedService> logger)
    {
        _processor = processor;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _options.IntervalSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await _processor.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "e-belge durum işi başarısız.");
            }
        }
    }
}
