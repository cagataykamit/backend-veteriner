using Backend.Veteriner.Application.Payments.ReadModels;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Query.Payments;

/// <summary>
/// Command DB <c>Payments</c> ile Query DB <c>PaymentReadModels</c> (list read-model) parity okuması (CQRS-14F).
/// Salt okunur (<c>AsNoTracking</c>); yazma/projection davranışına dokunmaz. Tenant + clinic kapsamlıdır.
///
/// Command tarafı recent örneğinde denormalize isim alanları (ClientName/PetName) backfill/projection ile
/// <b>aynı kurallarla</b> (trim) üretilir; böylece backfill sonrası karşılaştırma adildir. Not: ClientName/PetName
/// payment event'i ile snapshot'lanır; client/pet rename'i ayrı bir payment event'i tetiklemeden read-model'e
/// yansımaz — bu beklenen denormalizasyon davranışıdır (bkz. CQRS-14F dokümanı).
/// </summary>
public sealed class PaymentReadModelParityReader : IPaymentReadModelParityReader
{
    private readonly AppDbContext _commandDb;
    private readonly QueryDbContext _queryDb;

    public PaymentReadModelParityReader(AppDbContext commandDb, QueryDbContext queryDb)
    {
        _commandDb = commandDb;
        _queryDb = queryDb;
    }

    public async Task<PaymentReadModelParityResult> GetClinicParityAsync(
        Guid tenantId,
        Guid clinicId,
        int recentSampleSize = PaymentReadModelParityDefaults.RecentSampleSize,
        CancellationToken cancellationToken = default)
    {
        recentSampleSize = Math.Max(1, recentSampleSize);

        var commandPayments = _commandDb.Payments.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.ClinicId == clinicId);
        var queryReadModels = _queryDb.PaymentReadModels.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ClinicId == clinicId);

        var commandCount = await commandPayments.LongCountAsync(cancellationToken);
        var queryCount = await queryReadModels.LongCountAsync(cancellationToken);

        var commandRecent = await LoadCommandRecentAsync(
            tenantId, clinicId, recentSampleSize, cancellationToken);

        var queryRecentRows = await queryReadModels
            .OrderByDescending(x => x.PaidAtUtc)
            .ThenByDescending(x => x.PaymentId)
            .Take(recentSampleSize)
            .Select(x => new PaymentReadModelParityEvaluator.RowSnapshot(
                x.PaymentId,
                x.ClientId,
                x.PetId,
                x.Amount,
                x.Currency,
                x.Method,
                x.PaidAtUtc,
                x.ClientName,
                x.PetName,
                x.Notes))
            .ToListAsync(cancellationToken);

        return PaymentReadModelParityEvaluator.Evaluate(
            commandCount,
            queryCount,
            commandRecent,
            queryRecentRows,
            tenantId,
            clinicId);
    }

    private async Task<List<PaymentReadModelParityEvaluator.RowSnapshot>> LoadCommandRecentAsync(
        Guid tenantId,
        Guid clinicId,
        int recentSampleSize,
        CancellationToken cancellationToken)
    {
        var payments = await _commandDb.Payments.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.ClinicId == clinicId)
            .OrderByDescending(p => p.PaidAtUtc)
            .ThenByDescending(p => p.Id)
            .Take(recentSampleSize)
            .ToListAsync(cancellationToken);

        if (payments.Count == 0)
            return [];

        var clientIds = payments.Select(p => p.ClientId).Distinct().ToList();
        var clientNames = await _commandDb.Clients.AsNoTracking()
            .Where(c => clientIds.Contains(c.Id))
            .Select(c => new { c.Id, c.FullName })
            .ToDictionaryAsync(c => c.Id, c => c.FullName, cancellationToken);

        var petIds = payments.Where(p => p.PetId.HasValue).Select(p => p.PetId!.Value).Distinct().ToList();
        var petNames = petIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _commandDb.Pets.AsNoTracking()
                .Where(p => petIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Name })
                .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        var rows = new List<PaymentReadModelParityEvaluator.RowSnapshot>(payments.Count);
        foreach (var payment in payments)
        {
            clientNames.TryGetValue(payment.ClientId, out var clientFullName);
            var clientName = string.IsNullOrWhiteSpace(clientFullName) ? string.Empty : clientFullName.Trim();

            string? petName = null;
            if (payment.PetId is { } petId && petNames.TryGetValue(petId, out var rawPetName))
                petName = string.IsNullOrWhiteSpace(rawPetName) ? null : rawPetName.Trim();

            var notes = string.IsNullOrWhiteSpace(payment.Notes) ? null : payment.Notes.Trim();

            rows.Add(new PaymentReadModelParityEvaluator.RowSnapshot(
                payment.Id,
                payment.ClientId,
                payment.PetId,
                payment.Amount,
                payment.Currency,
                (int)payment.Method,
                payment.PaidAtUtc,
                clientName,
                petName,
                notes));
        }

        return rows;
    }
}
