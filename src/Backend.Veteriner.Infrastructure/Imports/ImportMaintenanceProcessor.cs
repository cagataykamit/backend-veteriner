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
public sealed record ImportMaintenanceResult(int ExpiredBatches, int PurgedBatches);

/// <summary>
/// İçe aktarma bakım işi (ADR-019 K9, IMPORT_API_CONTRACT.md §10): (1) 7 gün hareketsiz taslağı <c>Expired</c> yapar ve
/// hazırlık satırlarını aynı işlemde siler; (2) işi bitmiş partilerin satır verisini 30 gün sonra boşaltır
/// (<see cref="ImportBatchRow.PurgePayload"/>, parça parça; sonra <see cref="ImportBatch.PurgePayload"/>). Kurallar
/// domain'de ve seçim sorguları (<see cref="ImportBatchesDueExpirySpec"/>, <see cref="ImportBatchesDuePayloadPurgeSpec"/>)
/// Application'dadır; burada yalnız yürütme vardır. Her parti kendi kapsamında işlenir; çok örnekli kurulumda aynı
/// partiyi işleyen ikinci örnek rowversion'a çarpar. Günlüklere yalnız parti kimliği ve sayılar yazılır.
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
        foreach (var id in await DueIdsAsync(new ImportBatchesDueExpirySpec(Now, batchSize), ct))
        {
            if (await ExpireAsync(id, ct))
                expired++;
        }

        var purged = 0;
        foreach (var id in await DueIdsAsync(new ImportBatchesDuePayloadPurgeSpec(Now, batchSize), ct))
        {
            if (await PurgeAsync(id, ct))
                purged++;
        }

        if (expired + purged > 0)
            _logger.LogInformation("Import maintenance finished. Expired={Expired} Purged={Purged}", expired, purged);
        return new ImportMaintenanceResult(expired, purged);
    }

    private async Task<List<Guid>> DueIdsAsync(Ardalis.Specification.ISpecification<ImportBatch> spec, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ImportBatches.WithSpecification(spec, TenantSafeSpecificationEvaluator.Instance).Select(b => b.Id).ToListAsync(ct);
    }

    /// <summary>Süresi dolan taslak: durum ve satır silme tek işlemde (taslak silme ile aynı sonuç).</summary>
    private async Task<bool> ExpireAsync(Guid batchId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
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
    /// Satırlar parça parça boşaltılır, en sonda parti işaretlenir: yarıda kesilirse sonraki tur kaldığı yerden sürer
    /// (boşaltılmış satır yeniden seçilmez).
    /// </summary>
    private async Task<bool> PurgeAsync(Guid batchId, CancellationToken ct)
    {
        var purgedRows = 0;
        while (true)
        {
            await using var chunkScope = _scopes.CreateAsyncScope();
            var chunkDb = chunkScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await chunkDb.ImportBatchRows
                .Where(r => r.BatchId == batchId && (r.SourceJson != null || r.NormalizedJson != null || r.IssuesJson != null))
                .OrderBy(r => r.Id)
                .Take(RowChunk)
                .ToListAsync(ct);
            if (rows.Count == 0)
                break;

            foreach (var row in rows)
                row.PurgePayload();
            await chunkDb.SaveChangesAsync(ct);
            purgedRows += rows.Count;
        }

        await using var scope = _scopes.CreateAsyncScope();
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

        _logger.LogInformation("Import payload purged. Batch={BatchId} Rows={Rows}", batchId, purgedRows);
        await WriteAuditAsync(scope.ServiceProvider, batch, "ImportBatch.PayloadPurged", purgedRows, ct);
        return true;
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
