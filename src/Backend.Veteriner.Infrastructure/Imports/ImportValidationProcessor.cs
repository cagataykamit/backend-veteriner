using System.Text.Json;
using Backend.Veteriner.Application.Common.Auditing;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Imports;

/// <summary>
/// İçe aktarma doğrulama (kuru çalıştırma) işi (ADR-019 K13, IMPORT_API_CONTRACT.md §7.4). Uygulama işiyle aynı desen: iş
/// kuyruğu parti tablosudur, <c>Validating</c> ve kirası boş/dolmuş parti alınır (rowversion: iki örnekten biri). Kurallar
/// <see cref="ImportValidationRunner"/> içindedir; işçi yalnız çalıştırır ve yazar: önceki satır sonuçları kira yenilemesiyle
/// aynı işlemde sıfırlanır (ilerleme sayımı), yeni sonuçlar parça parça (<see cref="RowWriteChunk"/>; değişmeyen sonuçlar
/// yalnız durum sütunuyla, küme güncellemesiyle) ve her parçada kira
/// yenilenerek yazılır, sonunda parti <c>Validated</c> / <c>ValidationFailed</c> olur. Kira başka işçiye geçtiyse parça
/// yazılmaz (rowversion) ve eski işçi durur; çökme sonrası devralan işçi doğrulamayı baştan çalıştırır (ürün tablolarına
/// yazılmadığı için tekrar zararsızdır). Doğrulama outbox olayı üretmediğinden geri basınç uygulanmaz. Günlüklere yalnız
/// parti kimliği, sayılar ve kodlar yazılır (satır verisi kişisel veridir).
/// </summary>
public sealed class ImportValidationProcessor
{
    private const int RowWriteChunk = 500;
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ImportValidationProcessor> _logger;

    public ImportValidationProcessor(
        IServiceScopeFactory scopes,
        TimeProvider timeProvider,
        ILogger<ImportValidationProcessor> logger,
        string workerId)
    {
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > ImportBatch.MaxLeaseOwnerLength)
            throw new ArgumentException("İşçi kimliği boş olamaz ve kira sahibi uzunluğunu aşamaz.", nameof(workerId));

        _scopes = scopes;
        _timeProvider = timeProvider;
        _logger = logger;
        WorkerId = workerId;
    }

    /// <summary>Kira sahibi adı (örnek başına benzersiz).</summary>
    public string WorkerId { get; }

    private DateTime Now => _timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>Kuyruktaki en eski partiyi alıp sonuna kadar (veya kira kaybına kadar) işler; alınacak iş yoksa false.</summary>
    public Task<bool> ProcessNextAsync(CancellationToken ct) => ProcessAsync(only: null, ct);

    /// <summary>Yalnız verilen partiyi alınabiliyorsa işler (testler ve teşhis); alınamazsa false.</summary>
    public Task<bool> ProcessBatchAsync(Guid batchId, CancellationToken ct) => ProcessAsync(batchId, ct);

    private async Task<bool> ProcessAsync(Guid? only, CancellationToken ct)
    {
        var job = await TryClaimAsync(only, ct);
        if (job is not { } claimed)
            return false;

        await RunAsync(claimed.BatchId, claimed.TenantId, ct);
        return true;
    }

    private async Task<(Guid BatchId, Guid TenantId)?> TryClaimAsync(Guid? only, CancellationToken ct)
    {
        // Kuyruk taraması kiracılar arasıdır (ADR-040); partinin işi kendi kiracısının kapsamında döner.
        await using var scope = _scopes.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = Now;

        var query = db.ImportBatches
            .Where(b => b.Status == ImportBatchStatus.Validating
                        && (b.LeaseUntilUtc == null || b.LeaseUntilUtc <= now || b.LeaseOwner == WorkerId));
        if (only is { } id)
            query = query.Where(b => b.Id == id);

        var batch = await query.OrderBy(b => b.UpdatedAtUtc).FirstOrDefaultAsync(ct);
        if (batch is null || !batch.ClaimValidationLease(WorkerId, now).IsSuccess)
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
        _logger.LogInformation("Import validation started. Batch={BatchId} Worker={Worker}", batchId, WorkerId);
        try
        {
            await using var scope = _scopes.CreateTenantScope(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var batch = await db.ImportBatches.SingleOrDefaultAsync(b => b.Id == batchId, ct);
            if (batch is null || batch.Status != ImportBatchStatus.Validating || batch.LeaseOwner != WorkerId)
            {
                LogLeaseLost(batchId);
                return;
            }

            // Satırlar izlenir: sonuçlar aynı varlıklara yazılır (eski handler'daki tek okuma).
            var rows = await db.ImportBatchRows.Where(r => r.BatchId == batchId).ToListAsync(ct);
            var outcome = await scope.ServiceProvider.GetRequiredService<ImportValidationRunner>()
                .EvaluateAsync(batch, rows, Now, ct);

            if (outcome.HasRowResults && !await WriteRowResultsAsync(db, batch, rows, outcome, ct))
            {
                LogLeaseLost(batchId);
                return;
            }

            if (!outcome.Complete(batch, Now).IsSuccess || !await TrySaveAsync(db, ct))
            {
                LogLeaseLost(batchId);
                return;
            }

            _logger.LogInformation(
                "Import validation finished. Batch={BatchId} Status={Status} Rows={Rows} Customers={C}/{CW}/{CE} Pets={P}/{PW}/{PE} Vaccinations={V}/{VW}/{VE}",
                batchId, batch.Status, rows.Count,
                batch.CustomerCounts.Valid, batch.CustomerCounts.Warning, batch.CustomerCounts.Error,
                batch.PetCounts.Valid, batch.PetCounts.Warning, batch.PetCounts.Error,
                batch.VaccinationCounts.Valid, batch.VaccinationCounts.Warning, batch.VaccinationCounts.Error);
            await WriteAuditAsync(scope.ServiceProvider, batch, success: true, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Import validation interrupted by shutdown; lease expires and the job restarts. Batch={BatchId}", batchId);
        }
        catch (Exception ex)
        {
            // İstisna mesajı kişisel veri içerebilir: yalnız tür ve yığın yazılır.
            _logger.LogError(
                "Import validation failed unexpectedly. Batch={BatchId} Exception={ExceptionType} Inner={InnerType} Stack={Stack}",
                batchId, ex.GetType().FullName, ex.InnerException?.GetType().FullName, ex.StackTrace);
            await FailAsync(batchId, tenantId);
        }
    }

    /// <summary>
    /// Önceki sonuçlar sıfırlanır (ilerleme yalnız bu turun satırlarını sayar), sonra yeni sonuçlar parça parça yazılır.
    /// Sıfırlama ve her parça kira yenilemesiyle aynı işlemdedir: kira devralındıysa hiçbir şey yazılmaz, false döner.
    /// </summary>
    private async Task<bool> WriteRowResultsAsync(
        AppDbContext db, ImportBatch batch, List<ImportBatchRow> rows, ImportValidationOutcome outcome, CancellationToken ct)
    {
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            if (!batch.RenewValidationLease(WorkerId, Now).IsSuccess || !await TrySaveAsync(db, ct))
                return false;

            await db.ImportBatchRows
                .Where(r => r.BatchId == batch.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ValidationStatus, ImportRowValidationStatus.Pending), ct);
            await tx.CommitAsync(ct);
        }

        foreach (var chunk in rows.OrderBy(r => r.Table).ThenBy(r => r.RowNumber).Chunk(RowWriteChunk))
        {
            // İzlenen anlık görüntü sıfırlamadan önceki sonucu taşır. Sonucu değişen satır EF ile yazılır (durum sütunu
            // zorlanarak); sonucu aynı kalan satırın (yeniden doğrulamada çoğunluk) yalnız durumu küme güncellemesiyle
            // geri yazılır: satır başına komut olmaz.
            var unchanged = new Dictionary<ImportRowValidationStatus, List<Guid>>();
            foreach (var row in chunk)
            {
                outcome.ApplyTo(row);
                var entry = db.Entry(row);
                entry.DetectChanges();
                if (entry.State == EntityState.Unchanged)
                {
                    if (!unchanged.TryGetValue(row.ValidationStatus, out var ids))
                        unchanged[row.ValidationStatus] = ids = [];
                    ids.Add(row.Id);
                }
                else
                {
                    entry.Property(r => r.ValidationStatus).IsModified = true;
                }
            }

            await using (var tx = await db.Database.BeginTransactionAsync(ct))
            {
                if (!batch.RenewValidationLease(WorkerId, Now).IsSuccess || !await TrySaveAsync(db, ct))
                    return false;

                foreach (var (status, ids) in unchanged)
                {
                    await db.ImportBatchRows
                        .Where(r => r.BatchId == batch.Id && ids.Contains(r.Id))
                        .ExecuteUpdateAsync(s => s.SetProperty(r => r.ValidationStatus, status), ct);
                }

                await tx.CommitAsync(ct);
            }

            foreach (var row in chunk)
                db.Entry(row).State = EntityState.Detached;
        }

        return true;
    }

    /// <summary>Rowversion çakışması (kira başka işçide) false; bu durumda değişiklikler yazılmamıştır.</summary>
    private static async Task<bool> TrySaveAsync(AppDbContext db, CancellationToken ct)
    {
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

    /// <summary>Beklenmeyen hata: parti takılı kalmasın (yeniden doğrulanabilir). Kira başka işçideyse dokunulmaz.</summary>
    private async Task FailAsync(Guid batchId, Guid tenantId)
    {
        try
        {
            await using var scope = _scopes.CreateTenantScope(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var batch = await db.ImportBatches.SingleOrDefaultAsync(b => b.Id == batchId);
            if (batch is null || batch.LeaseOwner != WorkerId || !batch.FailValidation(Now).IsSuccess)
                return;

            await db.SaveChangesAsync();
            await WriteAuditAsync(scope.ServiceProvider, batch, success: false, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Kira dolunca başka işçi devralır ve yeniden dener.
            _logger.LogError("Import validation could not be marked failed. Batch={BatchId} Exception={ExceptionType}", batchId, ex.GetType().FullName);
        }
    }

    /// <summary>Arka plan tamamlanma kaydı: aktör/rota yok; payload yalnız durum ve sayılar (satır verisi yok).</summary>
    private static async Task WriteAuditAsync(IServiceProvider services, ImportBatch batch, bool success, CancellationToken ct)
    {
        static object Counts(ImportTableCounts c) => new { c.Total, c.Valid, c.Warning, c.Error };

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
                Action: "ImportBatch.ValidationCompleted",
                TargetType: nameof(ImportBatch),
                TargetId: $"ImportBatchId={batch.Id}",
                Success: success,
                FailureReason: success ? null : "Doğrulama beklenmeyen bir hatayla durdu.",
                Route: null,
                HttpMethod: null,
                IpAddress: null,
                UserAgent: null,
                CorrelationId: null,
                RequestName: nameof(ImportValidationProcessor),
                RequestPayload: payload,
                OccurredAtUtc: services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime),
            ct);
    }

    private void LogLeaseLost(Guid batchId)
        => _logger.LogWarning("Import validation lease lost; another worker owns the batch. Batch={BatchId} Worker={Worker}", batchId, WorkerId);
}
