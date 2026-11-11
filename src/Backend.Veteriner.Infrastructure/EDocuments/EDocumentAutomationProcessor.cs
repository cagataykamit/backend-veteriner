using System.Text.Json;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.EDocuments;
using Backend.Veteriner.Application.EDocuments.Automation;
using Backend.Veteriner.Application.EDocuments.Specs;
using Backend.Veteriner.Domain.EDocuments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.EDocuments;

/// <summary>
/// Otomatik e-belge ve gün sonu özeti turu (<see cref="EDocumentAutomation"/>). Bağlı klinikleri bulmak kiracılar arasıdır
/// (<c>AsSystem</c>); her klinik kendi kiracısının kapsamında, ayrı DI kapsamında işlenir. Otomatik kesilen her belge denetim
/// kaydına "otomatik" kaynağıyla yazılır (istek yok, aktör yok).
/// </summary>
public sealed class EDocumentAutomationProcessor
{
    public const string AuditAction = "EDocument.AutoIssue";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EDocumentAutomationProcessor> _logger;

    public EDocumentAutomationProcessor(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<EDocumentAutomationProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<ConnectedClinicItem> clinics;
        using (var scope = _scopeFactory.CreateScope())
        {
            var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantScope>();
            var connections = scope.ServiceProvider.GetRequiredService<IReadRepository<EDocumentConnection>>();
            using (tenantScope.AsSystem())
                clinics = await connections.ListAsync(new ConnectedEDocumentConnectionsSpec(), ct);
        }

        foreach (var clinic in clinics)
        {
            try
            {
                await RunClinicAsync(clinic, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Otomatik e-belge turu başarısız. ClinicId={ClinicId}", clinic.ClinicId);
            }
        }
    }

    private async Task RunClinicAsync(ConnectedClinicItem clinic, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        using var tenant = services.GetRequiredService<ITenantScope>().UseTenant(clinic.TenantId);

        var run = await services.GetRequiredService<EDocumentAutomation>().RunClinicAsync(clinic.TenantId, clinic.ClinicId, ct);
        var automatic = run.Documents.Where(d => d.Origin == EDocumentOrigin.Automatic).ToList();
        if (automatic.Count == 0 && run.Skipped.Count == 0)
            return;

        var db = services.GetRequiredService<AppDbContext>();
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var document in automatic)
        {
            db.AuditLogs.Add(new AuditLog(
                actorUserId: null,
                AuditAction,
                targetType: "EDocument",
                targetId: document.Id.ToString(),
                success: document.Status != EDocumentStatus.Failed,
                failureReason: document.Status == EDocumentStatus.Failed ? document.LastErrorMessage : null,
                route: null,
                httpMethod: null,
                ipAddress: null,
                userAgent: null,
                correlationId: null,
                requestName: nameof(EDocumentAutomation),
                requestPayload: JsonSerializer.Serialize(new
                {
                    origin = "Automatic",
                    clinicId = document.ClinicId,
                    documentType = document.Type.ToString(),
                    status = document.Status.ToString(),
                    chargeCount = document.Lines.Select(l => l.ChargeId).Distinct().Count(),
                }),
                occurredAtUtc: utcNow));
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Otomatik e-belge turu: ClinicId={ClinicId}, Kesilen={Issued}, Kesilemeyen ücret={Skipped}",
            clinic.ClinicId,
            automatic.Count,
            run.Skipped.Count);
    }
}

public sealed class EDocumentAutomationHostedService : BackgroundService
{
    private readonly EDocumentAutomationProcessor _processor;
    private readonly EDocumentProcessorOptions _options;
    private readonly ILogger<EDocumentAutomationHostedService> _logger;

    public EDocumentAutomationHostedService(
        EDocumentAutomationProcessor processor,
        IOptions<EDocumentProcessorOptions> options,
        ILogger<EDocumentAutomationHostedService> logger)
    {
        _processor = processor;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(15, _options.AutomationIntervalSeconds)));
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
                _logger.LogError(ex, "Otomatik e-belge işi başarısız.");
            }
        }
    }
}
