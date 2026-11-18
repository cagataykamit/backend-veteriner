using Backend.Veteriner.Application.Ai;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Ai;

/// <summary>AI kullanım toplamları (yalnız okur). Sistem toplamı ve klinik dökümü kiracılar arasıdır (ADR-044 madde 8).</summary>
public sealed class AiUsageReader : IAiUsageReader
{
    private readonly AppDbContext _db;
    private readonly ITenantScope _tenantScope;

    public AiUsageReader(AppDbContext db, ITenantScope tenantScope)
    {
        _db = db;
        _tenantScope = tenantScope;
    }

    public Task<int> CountUserRequestsSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken ct)
        => _db.AiUsageRecords.AsNoTracking()
            .CountAsync(r => r.UserId == userId && r.CreatedAtUtc >= sinceUtc, ct);

    public async Task<AiUsageTotals> ClinicTotalsAsync(Guid clinicId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
        => await TotalsAsync(_db.AiUsageRecords.AsNoTracking()
            .Where(r => r.ClinicId == clinicId && r.CreatedAtUtc >= fromUtc && r.CreatedAtUtc < toUtc), ct);

    public async Task<AiUsageTotals> SystemTotalsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        // Sistem geneli harcama sınırı tüm kiracılarda toplanır.
        using var system = _tenantScope.AsSystem();
        return await TotalsAsync(_db.AiUsageRecords.AsNoTracking()
            .Where(r => r.CreatedAtUtc >= fromUtc && r.CreatedAtUtc < toUtc), ct);
    }

    public async Task<IReadOnlyList<AiClinicUsageRow>> ClinicBreakdownAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        // Platform yönetimi raporu: tüm kiracıların klinikleri.
        using var system = _tenantScope.AsSystem();
        var rows = await _db.AiUsageRecords.AsNoTracking()
            .Where(r => r.CreatedAtUtc >= fromUtc && r.CreatedAtUtc < toUtc)
            .GroupBy(r => new { r.TenantId, r.ClinicId })
            .Select(g => new
            {
                g.Key.TenantId,
                g.Key.ClinicId,
                Requests = g.Count(),
                Tokens = g.Sum(x => (long)x.InputTokens + x.OutputTokens),
                CostUsd = g.Sum(x => x.CostUsd),
            })
            .ToListAsync(ct);
        if (rows.Count == 0)
            return [];

        var clinicIds = rows.Select(r => r.ClinicId).ToList();
        var names = await _db.Clinics.AsNoTracking()
            .Where(c => clinicIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return rows
            .OrderByDescending(r => r.CostUsd)
            .Select(r => new AiClinicUsageRow(
                r.TenantId, r.ClinicId, names.GetValueOrDefault(r.ClinicId, string.Empty), r.Requests, r.Tokens, r.CostUsd))
            .ToList();
    }

    private static async Task<AiUsageTotals> TotalsAsync(IQueryable<Domain.Ai.AiUsageRecord> query, CancellationToken ct)
    {
        var totals = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Requests = g.Count(),
                Tokens = g.Sum(x => (long)x.InputTokens + x.OutputTokens),
                CostUsd = g.Sum(x => x.CostUsd),
            })
            .FirstOrDefaultAsync(ct);

        return totals is null ? AiUsageTotals.Zero : new AiUsageTotals(totals.Requests, totals.Tokens, totals.CostUsd);
    }
}
