using System.Text.Json;
using Backend.Veteriner.Application.Common.Auditing;
using Backend.Veteriner.Application.Imports.Commands.ApplyOwnerUnit;
using Backend.Veteriner.Application.Imports.Commands.RollbackOwnerUnit;
using Backend.Veteriner.Application.Imports.Commit;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Rollback;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Infrastructure.Persistence;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Imports;

/// <summary>
/// İçe aktarma geri alma işi (ADR-019 K7, IMPORT_API_CONTRACT.md §7.12). Uygulama işiyle aynı desen: iş kuyruğu parti
/// tablosudur, <c>RollingBack</c> ve kirası boş/dolmuş parti alınır (rowversion: iki örnekten biri), sahip birimleri
/// parça parça ve her parça öncesi geri basınçla (silme olayları da outbox'a gider) işlenir. <c>Fail</c> politikasında
/// iş başındaki plan engel gösterirse hiçbir şey silinmez ve parti <c>RollbackFailed</c> olur. Her birim kendi DI
/// kapsamında ve partinin kiracı kapsamında çalışır; çökme sonrası devralan işçi sonuçlanmış satırları atlar.
/// Günlüklere yalnız parti kimliği, sayılar ve kodlar yazılır.
/// </summary>
public sealed class ImportRollbackProcessor
{
    public const string BlockedFailureCode = "Imports.RollbackBlocked";
    private const string UnexpectedFailureCode = "Imports.RollbackUnexpectedError";
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopes;
    private readonly ImportCommitOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ImportRollbackProcessor> _logger;

    public ImportRollbackProcessor(
        IServiceScopeFactory scopes,
        IOptions<ImportCommitOptions> options,
        TimeProvider timeProvider,
        ILogger<ImportRollbackProcessor> logger,
        string workerId)
    {
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > ImportBatch.MaxLeaseOwnerLength)
            throw new ArgumentException("İşçi kimliği boş olamaz ve kira sahibi uzunluğunu aşamaz.", nameof(workerId));

        _scopes = scopes;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        WorkerId = workerId;
    }

    /// <summary>Kira sahibi adı (örnek başına benzersiz).</summary>
    public string WorkerId { get; }

    private DateTime Now => _timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>Bir partiyi alıp sonuna kadar (veya kira kaybına kadar) işler; alınacak iş yoksa false.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var job = await TryClaimAsync(ct);
        if (job is not { } claimed)
            return false;

        await RunAsync(claimed.BatchId, claimed.TenantId, claimed.Policy, ct);
        return true;
    }

    private async Task<(Guid BatchId, Guid TenantId, string Policy)?> TryClaimAsync(CancellationToken ct)
    {
        // Kuyruk taraması kiracılar arasıdır (ADR-040); partinin işi kendi kiracısının kapsamında döner.
        await using var scope = _scopes.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = Now;

        var batch = await db.ImportBatches
            .Where(b => b.Status == ImportBatchStatus.RollingBack
                        && (b.LeaseUntilUtc == null || b.LeaseUntilUtc <= now || b.LeaseOwner == WorkerId))
            .OrderBy(b => b.UpdatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (batch is null || !batch.ClaimRollbackLease(WorkerId, now).IsSuccess)
            return null;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null; // Başka işçi aynı anda aldı.
        }

        var policy = ImportOptionsDocument.Parse(batch.OptionsJson).Rollback?.Policy ?? ImportRollbackPolicy.Fail;
        return (batch.Id, batch.TenantId, policy);
    }

    private async Task RunAsync(Guid batchId, Guid tenantId, string policy, CancellationToken ct)
    {
        _logger.LogInformation("Import rollback started. Batch={BatchId} Policy={Policy} Worker={Worker}", batchId, policy, WorkerId);
        try
        {
            var plan = await PlanAsync(batchId, tenantId, ct);
            if (policy == ImportRollbackPolicy.Fail && plan.HasBlocked)
            {
                _logger.LogInformation(
                    "Import rollback blocked at start; nothing deleted. Batch={BatchId} Blocked={Blocked}",
                    batchId, plan.Total(ImportRollbackDecision.Keep));
                await FailAsync(batchId, tenantId, BlockedFailureCode);
                return;
            }

            var gate = new ImportBackpressureGate(_options.Backpressure, _timeProvider, _logger);
            var unitsDone = 0;
            foreach (var chunk in plan.Units.Chunk(Math.Max(1, _options.OwnersPerChunk)))
            {
                if (!await gate.WaitAsync(batchId, ReadPressureAsync, c => RenewLeaseAsync(batchId, tenantId, c), ct)
                    || !await RenewLeaseAsync(batchId, tenantId, ct))
                {
                    LogLeaseLost(batchId);
                    return;
                }

                foreach (var unit in chunk)
                {
                    if (!await RollbackUnitAsync(batchId, tenantId, policy, unit, ct))
                    {
                        LogLeaseLost(batchId);
                        return;
                    }
                }

                unitsDone += chunk.Length;
                _logger.LogInformation(
                    "Import rollback progress. Batch={BatchId} Units={Done}/{Total}", batchId, unitsDone, plan.Units.Count);
            }

            await CompleteAsync(batchId, tenantId, policy, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Import rollback interrupted by shutdown; lease expires and the job resumes. Batch={BatchId}", batchId);
        }
        catch (Exception ex)
        {
            // İstisna mesajı kişisel veri içerebilir: yalnız tür ve yığın yazılır.
            _logger.LogError(
                "Import rollback failed unexpectedly. Batch={BatchId} Exception={ExceptionType} Inner={InnerType} Stack={Stack}",
                batchId, ex.GetType().FullName, ex.InnerException?.GetType().FullName, ex.StackTrace);
            await FailAsync(batchId, tenantId, UnexpectedFailureCode);
        }
    }

    /// <summary>Tüm partinin planı (önizlemeyle aynı kural); birimler ve <c>Fail</c> politikasının başlangıç denetimi.</summary>
    private async Task<ImportRollbackPlan> PlanAsync(Guid batchId, Guid tenantId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateTenantScope(tenantId);
        var loader = scope.ServiceProvider.GetRequiredService<ImportRollbackPlanLoader>();
        return (await loader.LoadAsync(tenantId, batchId, rowIds: null, ct)).Plan;
    }

    private async Task<ImportOutboxPressure> ReadPressureAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IImportBackpressureProbe>().ReadAsync(ct);
    }

    /// <summary>Birimi tek işlemde geri alır; kira kaybında false (işçi durur, birim geri alınmıştır).</summary>
    private async Task<bool> RollbackUnitAsync(Guid batchId, Guid tenantId, string policy, ImportRollbackUnit unit, CancellationToken ct)
    {
        Result<ImportRollbackUnitResult> result;
        try
        {
            await using var scope = _scopes.CreateTenantScope(tenantId);
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            result = await sender.Send(new RollbackImportOwnerUnitCommand(batchId, WorkerId, unit.RowIds, policy), ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return false; // Parti rowversion'ı değişti: kira devralındı, birim geri alındı.
        }

        if (result.IsSuccess)
            return true;
        if (result.Error.Code == ImportOwnerUnitErrors.LeaseLostCode)
            return false;

        throw new InvalidOperationException($"Geri alma birimi başarısız: {result.Error.Code}");
    }

    private async Task<bool> RenewLeaseAsync(Guid batchId, Guid tenantId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var batch = await db.ImportBatches.SingleOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null || !batch.RenewRollbackLease(WorkerId, Now).IsSuccess)
            return false;

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private async Task CompleteAsync(Guid batchId, Guid tenantId, string policy, CancellationToken ct)
    {
        await using var scope = _scopes.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var batch = await db.ImportBatches.SingleOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null || batch.Status != ImportBatchStatus.RollingBack || batch.LeaseOwner != WorkerId)
        {
            LogLeaseLost(batchId);
            return;
        }

        var counts = await RowCountsAsync(db, batchId, ct);
        var pending = counts.Where(c => c.Status == ImportRowApplyStatus.Created).Sum(c => c.Count);
        var kept = counts.Where(c => c.Status == ImportRowApplyStatus.Kept).Sum(c => c.Count);
        var completed = pending == 0
            ? batch.CompleteRollback(partial: kept > 0, Now)
            : batch.FailRollback(Now);
        if (!completed.IsSuccess)
            return;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogLeaseLost(batchId);
            return;
        }

        if (pending > 0)
            _logger.LogError("Import rollback ended with unprocessed rows; marked RollbackFailed. Batch={BatchId} Pending={Pending}", batchId, pending);

        _logger.LogInformation(
            "Import rollback finished. Batch={BatchId} Status={Status} Kept={Kept}", batchId, batch.Status, kept);
        await WriteAuditAsync(scope.ServiceProvider, batch, policy, counts, pending == 0 ? null : UnexpectedFailureCode, ct);
    }

    private async Task FailAsync(Guid batchId, Guid tenantId, string failureCode)
    {
        try
        {
            await using var scope = _scopes.CreateTenantScope(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var batch = await db.ImportBatches.SingleOrDefaultAsync(b => b.Id == batchId);
            if (batch is null || batch.LeaseOwner != WorkerId || !batch.FailRollback(Now).IsSuccess)
                return;

            await db.SaveChangesAsync();
            var policy = ImportOptionsDocument.Parse(batch.OptionsJson).Rollback?.Policy ?? ImportRollbackPolicy.Fail;
            await WriteAuditAsync(scope.ServiceProvider, batch, policy, await RowCountsAsync(db, batchId, CancellationToken.None), failureCode, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Kira dolunca başka işçi devralır ve yeniden dener.
            _logger.LogError("Import rollback could not be marked failed. Batch={BatchId} Exception={ExceptionType}", batchId, ex.GetType().FullName);
        }
    }

    private static async Task<List<RowCount>> RowCountsAsync(AppDbContext db, Guid batchId, CancellationToken ct)
        => await db.ImportBatchRows
            .AsNoTracking()
            .Where(r => r.BatchId == batchId
                        && (r.ApplyStatus == ImportRowApplyStatus.Created
                            || r.ApplyStatus == ImportRowApplyStatus.RolledBack
                            || r.ApplyStatus == ImportRowApplyStatus.Kept))
            .GroupBy(r => new { r.Table, r.ApplyStatus })
            .Select(g => new RowCount(g.Key.Table, g.Key.ApplyStatus, g.Count()))
            .ToListAsync(ct);

    /// <summary>Arka plan tamamlanma kaydı: aktör/rota yok; payload yalnız durum, politika ve sayılar (satır verisi yok).</summary>
    private static async Task WriteAuditAsync(
        IServiceProvider services,
        ImportBatch batch,
        string policy,
        IReadOnlyList<RowCount> counts,
        string? failureCode,
        CancellationToken ct)
    {
        object Table(ImportTable table) => new
        {
            Deleted = counts.Where(c => c.Table == table && c.Status == ImportRowApplyStatus.RolledBack).Sum(c => c.Count),
            Kept = counts.Where(c => c.Table == table && c.Status == ImportRowApplyStatus.Kept).Sum(c => c.Count),
        };

        var payload = JsonSerializer.Serialize(
            new
            {
                Status = batch.Status.ToString(),
                BlockedPolicy = policy,
                Customers = Table(ImportTable.Customers),
                Pets = Table(ImportTable.Pets),
                Vaccinations = Table(ImportTable.Vaccinations),
            },
            AuditJson);

        await services.GetRequiredService<IAuditLogWriter>().WriteAsync(
            new AuditLogEntry(
                ActorUserId: null,
                Action: "ImportBatch.RollbackCompleted",
                TargetType: nameof(ImportBatch),
                TargetId: $"ImportBatchId={batch.Id}",
                Success: failureCode is null,
                FailureReason: failureCode,
                Route: null,
                HttpMethod: null,
                IpAddress: null,
                UserAgent: null,
                CorrelationId: null,
                RequestName: nameof(ImportRollbackProcessor),
                RequestPayload: payload,
                OccurredAtUtc: services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime),
            ct);
    }

    private void LogLeaseLost(Guid batchId)
        => _logger.LogWarning("Import rollback lease lost; another worker owns the batch. Batch={BatchId} Worker={Worker}", batchId, WorkerId);

    private sealed record RowCount(ImportTable Table, ImportRowApplyStatus Status, int Count);
}
