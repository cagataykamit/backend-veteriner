using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Imports.Commit;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.Veteriner.Infrastructure.Projections.Clients;
using Backend.Veteriner.Infrastructure.Projections.Pets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Imports;

/// <summary>
/// İçe aktarma geri basıncı için outbox ölçümü (üç sorgu). Süzgeçler ve eşikler sağlık kontrolleriyle aynı kaynaktan
/// gelir: genel kuyruk <c>OutboxHealthCheck</c> (projeksiyon olayları hariç, <c>OutboxHealth:PendingDegradedThreshold</c>),
/// müşteri/hasta kuyrukları projeksiyon durum okuyucularının bekleyen tanımı ve <c>*ProjectionHealth:DegradedAfterSeconds</c>.
/// </summary>
public sealed class ImportBackpressureProbe : IImportBackpressureProbe
{
    private readonly AppDbContext _db;
    private readonly OutboxHealthOptions _outboxHealth;
    private readonly ClientProjectionOptions _clientProjection;
    private readonly ClientProjectionHealthOptions _clientHealth;
    private readonly PetProjectionOptions _petProjection;
    private readonly PetProjectionHealthOptions _petHealth;
    private readonly TimeProvider _timeProvider;

    public ImportBackpressureProbe(
        AppDbContext db,
        IOptions<OutboxHealthOptions> outboxHealth,
        IOptions<ClientProjectionOptions> clientProjection,
        IOptions<ClientProjectionHealthOptions> clientHealth,
        IOptions<PetProjectionOptions> petProjection,
        IOptions<PetProjectionHealthOptions> petHealth,
        TimeProvider timeProvider)
    {
        _db = db;
        _outboxHealth = outboxHealth.Value;
        _clientProjection = clientProjection.Value;
        _clientHealth = clientHealth.Value;
        _petProjection = petProjection.Value;
        _petHealth = petHealth.Value;
        _timeProvider = timeProvider;
    }

    public async Task<ImportOutboxPressure> ReadAsync(CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var outboxPending = await OutboxMessageQueryFilters
            .ExcludingProjectionIntegrationEvents(_db.OutboxMessages.AsNoTracking())
            .CountAsync(m => m.ProcessedAtUtc == null && m.DeadLetterAtUtc == null, ct);

        var client = await ProjectionAsync(
            "client-projection",
            _clientProjection.Enabled,
            OutboxMessageQueryFilters.ClientIntegrationEventsOnly(_db.OutboxMessages.AsNoTracking()),
            _clientHealth.DegradedAfterSeconds,
            now,
            ct);
        var pet = await ProjectionAsync(
            "pet-projection",
            _petProjection.Enabled,
            OutboxMessageQueryFilters.PetIntegrationEventsOnly(_db.OutboxMessages.AsNoTracking()),
            _petHealth.DegradedAfterSeconds,
            now,
            ct);

        return new ImportOutboxPressure(outboxPending, _outboxHealth.PendingDegradedThreshold, [client, pet]);
    }

    /// <summary>Kapalı projeksiyon ölçülmez (olayları tasarım gereği tüketilmez, sağlığı etkilemez).</summary>
    private static async Task<ImportProjectionPressure> ProjectionAsync(
        string name,
        bool enabled,
        IQueryable<OutboxMessage> events,
        int degradedAfterSeconds,
        DateTime now,
        CancellationToken ct)
    {
        if (!enabled)
            return new ImportProjectionPressure(name, false, 0, null, degradedAfterSeconds);

        // Bekleyen tanımı projeksiyon durum okuyucularıyla aynı: işlenmemiş, dead-letter değil, yeniden deneme zamanı gelmiş.
        var stats = await events
            .Where(m => m.ProcessedAtUtc == null
                        && m.DeadLetterAtUtc == null
                        && (m.NextAttemptAtUtc == null || m.NextAttemptAtUtc <= now))
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Oldest = g.Min(m => (DateTime?)m.CreatedAtUtc) })
            .FirstOrDefaultAsync(ct);

        TimeSpan? age = stats?.Oldest is { } oldest ? now - oldest : null;
        return new ImportProjectionPressure(name, true, stats?.Count ?? 0, age, degradedAfterSeconds);
    }
}
