using System.Text.Json;
using Ardalis.Specification.EntityFrameworkCore;
using Backend.Veteriner.Application.Common.Auditing;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Imports;

/// <summary>Bakım işi ayarları (<c>Imports:Maintenance</c>; IMPORT_API_CONTRACT.md §10–11).</summary>
public sealed class ImportMaintenanceOptions
{
    public const string SectionName = "Imports:Maintenance";

    /// <summary>False ise hosted service çalışmaz (işlemci elle çağrılabilir kalır; testler).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Turlar arası süre (dakika).</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>Tur başına en çok işlenen parti sayısı (tür başına).</summary>
    public int BatchSize { get; set; } = 20;
}

/// <summary>Bir bakım turunun sonucu.</summary>
public sealed record ImportMaintenanceResult(int ExpiredBatches, int PurgedBatches, int DeletedBatches = 0);

/// <summary>
/// İçe aktarma bakım işi (IMPORT_API_CONTRACT.md §10.1, ADR-051 eki): (1) 7 gün hareketsiz taslağı <c>Expired</c> yapar ve
/// hazırlık satırlarını aynı işlemde siler; (2) geri alma süresi dolan aktarılmış partinin satırlarını parça parça siler, sonra
/// partiyi işaretler (<see cref="ImportBatch.PurgePayload"/>; parti ve özeti kalır); (3) kapanmış partiyi (tamamen geri alınmış,
/// silinmiş, süresi dolmuş) son hareketten geri alma süresi sonra kaydıyla siler: partiye başvuran kayıt varsa silmez, önce denetim
/// günlüğüne özet yazar. Kurallar domain'de ve seçim sorguları (<see cref="ImportBatchesDueExpirySpec"/>,
/// <see cref="ImportBatchesDuePayloadPurgeSpec"/>, <see cref="ImportBatchesDueDeletionSpec"/>) Application'dadır; burada yalnız yürütme
/// vardır. Her parti kendi kapsamında işlenir; çok örnekli kurulumda aynı partiyi işleyen ikinci örnek rowversion'a çarpar, satır
/// silme tekrarı zararsızdır. Günlüklere yalnız parti kimliği ve sayılar yazılır.
/// </summary>
public sealed class ImportMaintenanceProcessor
{
    private const int RowChunk = 500;
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopes;
    private readonly ImportMaintenanceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ImportMaintenanceProcessor> _logger;

    public ImportMaintenanceProcessor(
        IServiceScopeFactory scopes,
        IOptions<ImportMaintenanceOptions> options,
        TimeProvider timeProvider,
        ILogger<ImportMaintenanceProcessor> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    private DateTime Now => _timeProvider.GetUtcNow().UtcDateTime;

    public async Task<ImportMaintenanceResult> RunOnceAsync(CancellationToken ct)
    {
        var batchSize = Math.Max(1, _options.BatchSize);

        var expired = 0;
        foreach (var (id, tenantId) in await DueBatchesAsync(new ImportBatchesDueExpirySpec(Now, batchSize), ct))
        {
            if (await ExpireAsync(id, tenantId, ct))
                expired++;
        }

        var purged = 0;
        foreach (var (id, tenantId) in await DueBatchesAsync(new ImportBatchesDuePayloadPurgeSpec(Now, batchSize), ct))
        {
            if (await PurgeAsync(id, tenantId, ct))
                purged++;
        }

        var deleted = 0;
        foreach (var (id, tenantId) in await DueBatchesAsync(new ImportBatchesDueDeletionSpec(Now, batchSize), ct))
        {
            if (await DeleteBatchAsync(id, tenantId, ct))
                deleted++;
        }

        if (expired + purged + deleted > 0)
        {
            _logger.LogInformation(
                "Import maintenance finished. Expired={Expired} Purged={Purged} Deleted={Deleted}", expired, purged, deleted);
        }

        return new ImportMaintenanceResult(expired, purged, deleted);
    }

    /// <summary>Bakım taraması kiracılar arasıdır (ADR-040); her parti kendi kiracısının kapsamında işlenir.</summary>
    private async Task<List<(Guid Id, Guid TenantId)>> DueBatchesAsync(Ardalis.Specification.ISpecification<ImportBatch> spec, CancellationToken ct)
    {
        await using var scope = _scopes.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var due = await db.ImportBatches.WithSpecification(spec, TenantSafeSpecificationEvaluator.Instance)
            .Select(b => new { b.Id, b.TenantId })
            .ToListAsync(ct);
        return due.Select(b => (b.Id, b.TenantId)).ToList();
    }

    /// <summary>Süresi dolan taslak: durum ve satır silme tek işlemde (taslak silme ile aynı sonuç).</summary>
    private async Task<bool> ExpireAsync(Guid batchId, Guid tenantId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var batch = await db.ImportBatches.SingleOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null || !batch.Expire(Now).IsSuccess)
            return false; // Arada hareket gördü (eşleme/doğrulama) veya durumu değişti.

        int deletedRows;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            try
            {
                await db.SaveChangesAsync(ct);
                deletedRows = await db.ImportBatchRows.Where(r => r.BatchId == batchId).ExecuteDeleteAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(ct);
                return false;
            }
        }

        _logger.LogInformation("Import draft expired. Batch={BatchId} DeletedRows={Rows}", batchId, deletedRows);
        await WriteAuditAsync(scope.ServiceProvider, batch, "ImportBatch.Expired", deletedRows, ct);
        return true;
    }

    /// <summary>
    /// Geri alma süresi dolan aktarılmış parti: satırlar parça parça silinir, en sonda parti işaretlenir (kaydı ve özeti kalır). Yarıda
    /// kesilirse sonraki tur kaldığı yerden sürer.
    /// </summary>
    private async Task<bool> PurgeAsync(Guid batchId, Guid tenantId, CancellationToken ct)
    {
        var purgedRows = await DeleteRowsAsync(batchId, ct);

        await using var scope = _scopes.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var batch = await db.ImportBatches.SingleOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null || !batch.PurgePayload(Now).IsSuccess)
            return false; // Arada durum değişti (örn. geri alma başladı); süre yeni son değişiklikten sayılır.

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }

        _logger.LogInformation("Import rows deleted after rollback window. Batch={BatchId} Rows={Rows}", batchId, purgedRows);
        await WriteAuditAsync(scope.ServiceProvider, batch, "ImportBatch.PayloadPurged", purgedRows, ct);
        return true;
    }

    /// <summary>
    /// Kapanmış parti kaydıyla birlikte silinir: süre kuralı yeniden denetlenir (arada hareket görmüş olabilir), partiye başvuran kayıt
    /// varsa silinmez (günlüğe sayı yazılır, sonraki tur yeniden dener). Satırlar parça parça, sonra parti tek kısa işlemde silinir;
    /// denetim özeti silinmeden önce okunan kayıttan, silme başarılı olunca yazılır.
    /// </summary>
    private async Task<bool> DeleteBatchAsync(Guid batchId, Guid tenantId, CancellationToken ct)
    {
        await using (var checkScope = _scopes.CreateTenantScope(tenantId))
        {
            var db = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var batch = await db.ImportBatches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == batchId, ct);
            if (batch is null || !batch.IsDueForDeletion(Now))
                return false;

            var references = await ReferenceCountAsync(db, batchId, ct);
            if (references > 0)
            {
                _logger.LogWarning(
                    "Closed import batch not deleted; records still reference it. Batch={BatchId} Status={Status} References={References}",
                    batchId, batch.Status, references);
                return false;
            }
        }

        var deletedRows = await DeleteRowsAsync(batchId, ct);

        await using var scope = _scopes.CreateTenantScope(tenantId);
        var command = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var target = await command.ImportBatches.SingleOrDefaultAsync(b => b.Id == batchId, ct);
        if (target is null || !target.IsDueForDeletion(Now))
            return false; // Arada silindi ya da durum değişti.

        command.ImportBatches.Remove(target);
        try
        {
            await command.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        catch (DbUpdateException)
        {
            // Denetim ile silme arasında başvuran kayıt oluştu (NO ACTION yabancı anahtar): parti kalır, sonraki tur yeniden dener.
            _logger.LogWarning("Closed import batch not deleted; a reference appeared during deletion. Batch={BatchId}", batchId);
            return false;
        }

        // Özet silinmeden önce okunan kayıttan; silme başarılı olunca yazılır (başarısız silme yanıltıcı kayıt bırakmasın).
        await WriteDeletionAuditAsync(scope.ServiceProvider, target, deletedRows, ct);
        _logger.LogInformation("Closed import batch deleted. Batch={BatchId} Status={Status} Rows={Rows}", batchId, target.Status, deletedRows);
        return true;
    }

    /// <summary>Partiye <c>ImportBatchId</c> ile başvuran kayıtlar (yabancı anahtarlı tablolar; iptal edilmiş ücret dahil).</summary>
    private static async Task<int> ReferenceCountAsync(AppDbContext db, Guid batchId, CancellationToken ct)
        => await db.Vaccinations.IgnoreQueryFilters(QueryFilterNames.Ignorable).CountAsync(x => x.ImportBatchId == batchId, ct)
           + await db.Charges.IgnoreQueryFilters(QueryFilterNames.Ignorable).CountAsync(x => x.ImportBatchId == batchId, ct)
           + await db.ClientCredits.IgnoreQueryFilters(QueryFilterNames.Ignorable).CountAsync(x => x.ImportBatchId == batchId, ct)
           + await db.ImportedClinicalRecords.IgnoreQueryFilters(QueryFilterNames.Ignorable).CountAsync(x => x.ImportBatchId == batchId, ct)
           + await db.ImportedAccountEntries.IgnoreQueryFilters(QueryFilterNames.Ignorable).CountAsync(x => x.ImportBatchId == batchId, ct)
           + await db.Appointments.IgnoreQueryFilters(QueryFilterNames.Ignorable).CountAsync(x => x.ImportBatchId == batchId, ct);

    /// <summary>Partinin satırlarını kısa işlemlerle, <see cref="RowChunk"/>'lık parçalarla siler; silinen satır sayısı.</summary>
    private async Task<int> DeleteRowsAsync(Guid batchId, CancellationToken ct)
    {
        var deleted = 0;
        while (true)
        {
            await using var chunkScope = _scopes.CreateAsyncScope();
            var db = chunkScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ids = await db.ImportBatchRows.AsNoTracking()
                .Where(r => r.BatchId == batchId)
                .OrderBy(r => r.Id)
                .Select(r => r.Id)
                .Take(RowChunk)
                .ToListAsync(ct);
            if (ids.Count == 0)
                return deleted;

            deleted += await db.ImportBatchRows.Where(r => ids.Contains(r.Id)).ExecuteDeleteAsync(ct);
        }
    }

    /// <summary>
    /// Silinen partinin denetim özeti (ADR-051 eki): kim, ne zaman, kaç kayıt. Aktör yok (sistem); oluşturan kullanıcının kimliği payload'da.
    /// Ad, dosya adı ve satır verisi yazılmaz.
    /// </summary>
    private static async Task WriteDeletionAuditAsync(IServiceProvider services, ImportBatch batch, int rows, CancellationToken ct)
    {
        object Table(ImportTableCounts c) => new { c.Total, c.Created };
        var payload = JsonSerializer.Serialize(
            new
            {
                Status = batch.Status.ToString(),
                SourceProfile = batch.SourceProfile.ToString(),
                batch.ClinicId,
                batch.CreatedByUserId,
                batch.CreatedAtUtc,
                batch.CommittedAtUtc,
                batch.RolledBackAtUtc,
                LastChangedAtUtc = batch.UpdatedAtUtc,
                Rows = rows,
                Customers = Table(batch.CustomerCounts),
                Pets = Table(batch.PetCounts),
                Vaccinations = Table(batch.VaccinationCounts),
                ClinicalRecords = Table(batch.ClinicalRecordCounts),
                AccountEntries = Table(batch.AccountEntryCounts),
                Appointments = Table(batch.AppointmentCounts),
                Products = Table(batch.ProductCounts),
            },
            AuditJson);

        await services.GetRequiredService<IAuditLogWriter>().WriteAsync(
            new AuditLogEntry(
                ActorUserId: null,
                Action: "ImportBatch.Deleted",
                TargetType: nameof(ImportBatch),
                TargetId: $"ImportBatchId={batch.Id}",
                Success: true,
                FailureReason: null,
                Route: null,
                HttpMethod: null,
                IpAddress: null,
                UserAgent: null,
                CorrelationId: null,
                RequestName: nameof(ImportMaintenanceProcessor),
                RequestPayload: payload,
                OccurredAtUtc: services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime),
            ct);
    }

    /// <summary>Sistem kaydı: aktör yok; payload yalnız satır sayısı (satır verisi yok).</summary>
    private static async Task WriteAuditAsync(IServiceProvider services, ImportBatch batch, string action, int rows, CancellationToken ct)
        => await services.GetRequiredService<IAuditLogWriter>().WriteAsync(
            new AuditLogEntry(
                ActorUserId: null,
                Action: action,
                TargetType: nameof(ImportBatch),
                TargetId: $"ImportBatchId={batch.Id}",
                Success: true,
                FailureReason: null,
                Route: null,
                HttpMethod: null,
                IpAddress: null,
                UserAgent: null,
                CorrelationId: null,
                RequestName: nameof(ImportMaintenanceProcessor),
                RequestPayload: JsonSerializer.Serialize(new { Status = batch.Status.ToString(), Rows = rows }, AuditJson),
                OccurredAtUtc: services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime),
            ct);
}
