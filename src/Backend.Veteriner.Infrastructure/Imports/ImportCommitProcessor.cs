using System.Text.Json;
using Backend.Veteriner.Application.Common.Auditing;
using Backend.Veteriner.Application.Imports.Commands.ApplyOwnerUnit;
using Backend.Veteriner.Application.Imports.Commit;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Web;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Imports;

/// <summary>
/// İçe aktarma uygulama işi (ADR-019 K11, IMPORT_API_CONTRACT.md §6.5). İş kuyruğu parti tablosudur: <c>Committing</c> ve
/// kirası boş/dolmuş parti alınır (rowversion: iki örnekten biri), hatalı satırlar <c>Skipped</c> yazılır, sahip birimleri
/// parça parça (<see cref="ImportCommitOptions.OwnersPerChunk"/>) ve her parça öncesi geri basınçla uygulanır, sonunda
/// sayaçlar satır sonuçlarından hesaplanıp parti <c>Committed</c> olur. Her birim kendi DI kapsamında (temiz
/// DbContext) ve partinin kiracı kapsamında çalışır. Çökme sonrası devralan işçi yazılmış satırları atlar. Günlüklere
/// yalnız parti kimliği, sayılar ve kodlar yazılır (satır verisi kişisel veridir).
/// </summary>
public sealed class ImportCommitProcessor
{
    private const int RowWriteChunk = 500;
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopes;
    private readonly ImportCommitOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ImportCommitProcessor> _logger;

    public ImportCommitProcessor(
        IServiceScopeFactory scopes,
        IOptions<ImportCommitOptions> options,
        TimeProvider timeProvider,
        ILogger<ImportCommitProcessor> logger,
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

    public static string NewWorkerId()
    {
        var id = $"import:{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        return id.Length <= ImportBatch.MaxLeaseOwnerLength ? id : id[^ImportBatch.MaxLeaseOwnerLength..];
    }

    private DateTime Now => _timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>Bir partiyi alıp sonuna kadar (veya kira kaybına kadar) işler; alınacak iş yoksa false.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var job = await TryClaimAsync(ct);
        if (job is not { } claimed)
            return false;

        await RunAsync(claimed.BatchId, claimed.TenantId, ct);
        return true;
    }

    private async Task<(Guid BatchId, Guid TenantId)?> TryClaimAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = Now;

        var batch = await db.ImportBatches
            .Where(b => b.Status == ImportBatchStatus.Committing
                        && (b.LeaseUntilUtc == null || b.LeaseUntilUtc <= now || b.LeaseOwner == WorkerId))
            .OrderBy(b => b.UpdatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (batch is null || !batch.ClaimCommitLease(WorkerId, now).IsSuccess)
            return null;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null; // Başka işçi aynı anda aldı.
        }

        return (batch.Id, batch.TenantId);
    }

    private async Task RunAsync(Guid batchId, Guid tenantId, CancellationToken ct)
    {
        _logger.LogInformation("Import commit started. Batch={BatchId} Worker={Worker}", batchId, WorkerId);
        try
        {
            var plan = ImportCommitPlanner.Build(await LoadRowsAsync(batchId, ct));
            if (!await SkipRowsAsync(batchId, plan.RowsToSkip, ct))
            {
                LogLeaseLost(batchId);
                return;
            }

            var gate = new ImportBackpressureGate(_options.Backpressure, _timeProvider, _logger);
            var unitsDone = 0;
            foreach (var chunk in plan.Units.Chunk(Math.Max(1, _options.OwnersPerChunk)))
            {
                if (!await gate.WaitAsync(batchId, ReadPressureAsync, c => RenewLeaseAsync(batchId, c), ct)
                    || !await RenewLeaseAsync(batchId, ct))
                {
                    LogLeaseLost(batchId);
                    return;
                }

                foreach (var unit in chunk)
                {
                    if (!await ApplyUnitAsync(batchId, tenantId, unit, ct))
                    {
                        LogLeaseLost(batchId);
                        return;
                    }
                }

                unitsDone += chunk.Length;
                _logger.LogInformation(
                    "Import commit progress. Batch={BatchId} Units={Done}/{Total}", batchId, unitsDone, plan.Units.Count);
            }

            await CompleteAsync(batchId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Import commit interrupted by shutdown; lease expires and the job resumes. Batch={BatchId}", batchId);
        }
        catch (Exception ex)
        {
            // İstisna mesajı (örn. benzersiz anahtar ihlali) kişisel veri içerebilir: yalnız tür ve yığın yazılır.
            _logger.LogError(
                "Import commit failed unexpectedly. Batch={BatchId} Exception={ExceptionType} Inner={InnerType} Stack={Stack}",
                batchId, ex.GetType().FullName, ex.InnerException?.GetType().FullName, ex.StackTrace);
            await FailAsync(batchId);
        }
    }

    private async Task<IReadOnlyList<ImportCommitRowInfo>> LoadRowsAsync(Guid batchId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ImportBatchRows
            .AsNoTracking()
            .Where(r => r.BatchId == batchId)
            .Select(r => new ImportCommitRowInfo(r.Id, r.Table, r.RowNumber, r.ValidationStatus, r.ApplyStatus, r.NormalizedJson))
            .ToListAsync(ct);
    }

    private async Task<ImportOutboxPressure> ReadPressureAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IImportBackpressureProbe>().ReadAsync(ct);
    }

    /// <summary>Uygulanmayacak satırlar <c>Skipped</c> yazılır; kira aynı kayıtta yenilenir (devralındıysa false).</summary>
    private async Task<bool> SkipRowsAsync(Guid batchId, IReadOnlyList<Guid> rowIds, CancellationToken ct)
    {
        foreach (var ids in rowIds.Chunk(RowWriteChunk))
        {
            var written = await WriteRowsAsync(batchId, ids, row =>
                row.RecordApplyResult(ImportRowApplyStatus.Skipped, null, null, matched: false, Now), ct);
            if (!written)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Birimi uygular; yarışta mükerrer (veya benzersiz indeks ihlali) olursa birim bir kez yeniden çözülüp denenir.
    /// Kira kaybında false (işçi durur).
    /// </summary>
    private async Task<bool> ApplyUnitAsync(Guid batchId, Guid tenantId, ImportOwnerUnitPlan unit, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            Result<ImportOwnerUnitResult> result;
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                using var tenantScope = BackgroundTenantScope.Begin(tenantId);
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                result = await sender.Send(new ApplyImportOwnerUnitCommand(batchId, WorkerId, unit.OwnerKey, unit.RowIds), ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return false; // Parti rowversion'ı değişti: kira devralındı, birim geri alındı.
            }
            catch (DbUpdateException ex)
            {
                if (attempt == 1)
                    continue;

                _logger.LogWarning(
                    "Import unit failed after retry. Batch={BatchId} Rows={RowCount} Exception={ExceptionType}",
                    batchId, unit.RowIds.Count, ex.InnerException?.GetType().FullName ?? ex.GetType().FullName);
                return await MarkUnitFailedAsync(batchId, unit, new Error(ImportRules.ApplyFailed.Code, ImportRules.ApplyFailed.Message), ct);
            }

            if (result.IsSuccess)
                return true;
            if (result.Error.Code == ImportOwnerUnitErrors.LeaseLostCode)
                return false;
            if (attempt == 1 && ImportOwnerUnitErrors.IsRetryable(result.Error))
                continue;

            _logger.LogInformation(
                "Import unit failed. Batch={BatchId} Rows={RowCount} Code={Code}", batchId, unit.RowIds.Count, result.Error.Code);
            return await MarkUnitFailedAsync(batchId, unit, result.Error, ct);
        }
    }

    /// <summary>Başarısız birim: neden satırı komutun koduyla, diğerleri <c>Imports.OwnerUnitFailed</c> ile <c>Failed</c>.</summary>
    private async Task<bool> MarkUnitFailedAsync(Guid batchId, ImportOwnerUnitPlan unit, Error error, CancellationToken ct)
    {
        var failingRowId = ImportOwnerUnitErrors.RowIdOf(error) ?? unit.RowIds[0];
        return await WriteRowsAsync(batchId, unit.RowIds.ToArray(), row =>
        {
            var issue = row.Id == failingRowId
                ? new ImportIssue(ImportIssueSeverity.Error, error.Code, null, error.Message)
                : ImportRules.OwnerUnitFailed.At();
            row.RecordApplyResult(ImportRowApplyStatus.Failed, null, null, matched: false, Now, ImportIssueJson.Append(row.IssuesJson, issue));
        }, ct);
    }

    /// <summary>
    /// Bekleyen satırları yazar ve kirayı aynı <c>SaveChanges</c>'te yeniler: kira başka işçideyse hiçbir şey yazılmaz
    /// (rowversion), false döner.
    /// </summary>
    private async Task<bool> WriteRowsAsync(Guid batchId, Guid[] rowIds, Action<ImportBatchRow> write, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var batch = await db.ImportBatches.SingleOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null || !batch.RenewCommitLease(WorkerId, Now).IsSuccess)
            return false;

        if (rowIds.Length > 0)
        {
            var rows = await db.ImportBatchRows
                .Where(r => r.BatchId == batchId && rowIds.Contains(r.Id) && r.ApplyStatus == ImportRowApplyStatus.Pending)
                .ToListAsync(ct);
            foreach (var row in rows)
                write(row);
        }

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

    private async Task<bool> RenewLeaseAsync(Guid batchId, CancellationToken ct)
        => await WriteRowsAsync(batchId, [], _ => { }, ct);

    private async Task CompleteAsync(Guid batchId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var batch = await db.ImportBatches.SingleOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null || batch.Status != ImportBatchStatus.Committing || batch.LeaseOwner != WorkerId)
        {
            LogLeaseLost(batchId);
            return;
        }

        var counts = await db.ImportBatchRows
            .AsNoTracking()
            .Where(r => r.BatchId == batchId)
            .GroupBy(r => new { r.Table, r.ApplyStatus })
            .Select(g => new { g.Key.Table, g.Key.ApplyStatus, Count = g.Count() })
            .ToListAsync(ct);

        int Count(ImportTable table, ImportRowApplyStatus status)
            => counts.Where(c => c.Table == table && c.ApplyStatus == status).Sum(c => c.Count);

        ImportTableCounts Applied(ImportTableCounts validation, ImportTable table) => validation with
        {
            Created = Count(table, ImportRowApplyStatus.Created),
            Matched = Count(table, ImportRowApplyStatus.Matched),
            Skipped = Count(table, ImportRowApplyStatus.Skipped),
            Failed = Count(table, ImportRowApplyStatus.Failed),
        };

        var pending = counts.Where(c => c.ApplyStatus == ImportRowApplyStatus.Pending).Sum(c => c.Count);
        var now = Now;
        var completed = pending == 0
            ? batch.CompleteCommit(
                Applied(batch.CustomerCounts, ImportTable.Customers),
                Applied(batch.PetCounts, ImportTable.Pets),
                Applied(batch.VaccinationCounts, ImportTable.Vaccinations),
                now)
            : batch.FailCommit(now);
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
            _logger.LogError("Import commit ended with unapplied rows; marked CommitFailed. Batch={BatchId} Pending={Pending}", batchId, pending);

        _logger.LogInformation(
            "Import commit finished. Batch={BatchId} Status={Status} Customers={C}/{CM}/{CS}/{CF} Pets={P}/{PM}/{PS}/{PF} Vaccinations={V}/{VM}/{VS}/{VF}",
            batchId, batch.Status,
            batch.CustomerCounts.Created, batch.CustomerCounts.Matched, batch.CustomerCounts.Skipped, batch.CustomerCounts.Failed,
            batch.PetCounts.Created, batch.PetCounts.Matched, batch.PetCounts.Skipped, batch.PetCounts.Failed,
            batch.VaccinationCounts.Created, batch.VaccinationCounts.Matched, batch.VaccinationCounts.Skipped, batch.VaccinationCounts.Failed);

        await WriteAuditAsync(scope.ServiceProvider, batch, success: pending == 0, ct);
    }

    private async Task FailAsync(Guid batchId)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var batch = await db.ImportBatches.SingleOrDefaultAsync(b => b.Id == batchId);
            if (batch is null || batch.LeaseOwner != WorkerId || !batch.FailCommit(Now).IsSuccess)
                return;

            await db.SaveChangesAsync();
            await WriteAuditAsync(scope.ServiceProvider, batch, success: false, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Kira dolunca başka işçi devralır ve yeniden dener.
            _logger.LogError("Import commit could not be marked failed. Batch={BatchId} Exception={ExceptionType}", batchId, ex.GetType().FullName);
        }
    }

    /// <summary>Arka plan tamamlanma kaydı: aktör/rota yok; payload yalnız durum ve sayılar (satır verisi yok).</summary>
    private static async Task WriteAuditAsync(IServiceProvider services, ImportBatch batch, bool success, CancellationToken ct)
    {
        static object Counts(ImportTableCounts c) => new { c.Created, c.Matched, c.Skipped, c.Failed };

        var payload = JsonSerializer.Serialize(
            new
            {
                Status = batch.Status.ToString(),
                Customers = Counts(batch.CustomerCounts),
                Pets = Counts(batch.PetCounts),
                Vaccinations = Counts(batch.VaccinationCounts),
            },
            AuditJson);

        await services.GetRequiredService<IAuditLogWriter>().WriteAsync(
            new AuditLogEntry(
                ActorUserId: null,
                Action: "ImportBatch.CommitCompleted",
                TargetType: nameof(ImportBatch),
                TargetId: $"ImportBatchId={batch.Id}",
                Success: success,
                FailureReason: success ? null : "Uygulama beklenmeyen bir hatayla durdu.",
                Route: null,
                HttpMethod: null,
                IpAddress: null,
                UserAgent: null,
                CorrelationId: null,
                RequestName: nameof(ImportCommitProcessor),
                RequestPayload: payload,
                OccurredAtUtc: services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime),
            ct);
    }

    private void LogLeaseLost(Guid batchId)
        => _logger.LogWarning("Import commit lease lost; another worker owns the batch. Batch={BatchId} Worker={Worker}", batchId, WorkerId);
}
