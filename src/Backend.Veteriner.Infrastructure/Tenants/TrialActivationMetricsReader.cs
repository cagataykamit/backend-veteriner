using Backend.Veteriner.Application.Tenants.Activation;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Tenants;

/// <summary>Deneme kohortu: dönemde başlayan abonelikler + aktivasyon kaydı (sol birleşim). Yalnız okur.</summary>
public sealed class TrialActivationMetricsReader : ITrialActivationMetricsReader
{
    private readonly AppDbContext _db;

    public TrialActivationMetricsReader(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<TrialCohortRow>> ListCohortAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var rows = await (
            from s in _db.TenantSubscriptions.AsNoTracking()
            where s.TrialStartsAtUtc != null && s.TrialStartsAtUtc >= fromUtc && s.TrialStartsAtUtc < toUtc
            join a0 in _db.TenantActivations.AsNoTracking() on s.TenantId equals a0.TenantId into activations
            from a in activations.DefaultIfEmpty()
            orderby s.TrialStartsAtUtc
            select new TrialCohortRow(
                s.TenantId,
                s.TrialStartsAtUtc!.Value,
                s.ActivatedAtUtc,
                a != null ? a.ExampleClinicLoadedAtUtc : null,
                a != null ? a.FirstActionAtUtc : null,
                a != null ? a.FirstActionKind : null))
            .ToListAsync(ct);

        return rows;
    }
}
