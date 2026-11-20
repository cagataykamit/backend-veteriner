using System.Linq.Expressions;
using Backend.Veteriner.Application.Ai;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Ai;

/// <summary>
/// AI kullanım toplamları (yalnız okur). Sistem toplamı, birden çok kliniğin kredi toplamı ve platform raporu kiracılar arasıdır
/// (ADR-044 madde 8; platform yöneticisi). Klinik metin okunmaz: yalnız sayılar, model ve özellik.
/// </summary>
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

    public async Task<AiClinicCreditTotals> ClinicCreditTotalsAsync(
        Guid clinicId, DateTime monthStartUtc, DateTime monthEndUtc, CancellationToken ct)
        => (await CreditTotalsAsync([clinicId], monthStartUtc, monthEndUtc, ct)).GetValueOrDefault(clinicId, AiClinicCreditTotals.Zero);

    public async Task<IReadOnlyDictionary<Guid, AiClinicCreditTotals>> ClinicsCreditTotalsAsync(
        IReadOnlyCollection<Guid> clinicIds, DateTime monthStartUtc, DateTime monthEndUtc, CancellationToken ct)
    {
        // Platform yönetimi: farklı kiracıların klinikleri.
        using var system = _tenantScope.AsSystem();
        return await CreditTotalsAsync(clinicIds, monthStartUtc, monthEndUtc, ct);
    }

    public async Task<AiUsageTotals> SystemTotalsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        // Sistem geneli harcama sınırı tüm kiracılarda toplanır.
        using var system = _tenantScope.AsSystem();
        var totals = await _db.AiUsageRecords.AsNoTracking()
            .Where(r => r.CreatedAtUtc >= fromUtc && r.CreatedAtUtc < toUtc)
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

    public async Task<AiUsageBreakdown> BreakdownAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        // Platform yönetimi raporu (AI-008): tüm kiracılar.
        using var system = _tenantScope.AsSystem();
        var month = _db.AiUsageRecords.AsNoTracking().Where(r => r.CreatedAtUtc >= fromUtc && r.CreatedAtUtc < toUtc);

        var total = (await GroupAsync(month, r => 1, ct)).Select(x => x.Stats).FirstOrDefault() ?? AiUsageStats.Zero;
        var byClinic = await GroupAsync(month, r => new { r.TenantId, r.ClinicId }, ct);
        var byFeature = await GroupAsync(month, r => r.Feature, ct);
        var byModel = await GroupAsync(month, r => r.Model, ct);
        var byUser = (await GroupAsync(month, r => new { r.UserId, r.TenantId, r.ClinicId }, ct))
            .OrderByDescending(x => x.Stats.CostUsd)
            .ThenByDescending(x => x.Stats.Requests)
            .Take(AiUsageBreakdown.MaxUserRows)
            .ToList();

        var userIds = byUser.Select(x => x.Key.UserId).Distinct().ToList();
        var users = userIds.Count == 0
            ? new Dictionary<Guid, (string Name, string Email)>()
            : (await _db.Users.AsNoTracking()
                    .Where(u => userIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.DisplayName, u.Email })
                    .ToListAsync(ct))
                .ToDictionary(u => u.Id, u => (u.DisplayName ?? string.Empty, u.Email));

        var clinicIds = byClinic.Select(x => x.Key.ClinicId).ToList();
        var tenantIds = byClinic.Select(x => x.Key.TenantId).Distinct().ToList();
        var clinicNames = clinicIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Clinics.AsNoTracking().Where(c => clinicIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var tenantNames = tenantIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Tenants.AsNoTracking().Where(t => tenantIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        return new AiUsageBreakdown(
            total,
            byClinic
                .OrderByDescending(x => x.Stats.CostUsd)
                .Select(x => new AiClinicUsageRow(
                    x.Key.TenantId,
                    tenantNames.GetValueOrDefault(x.Key.TenantId, string.Empty),
                    x.Key.ClinicId,
                    clinicNames.GetValueOrDefault(x.Key.ClinicId, string.Empty),
                    x.Stats))
                .ToList(),
            byFeature.OrderByDescending(x => x.Stats.CostUsd).Select(x => new AiFeatureUsageRow(x.Key, x.Stats)).ToList(),
            byModel.OrderByDescending(x => x.Stats.CostUsd).Select(x => new AiModelUsageRow(x.Key, x.Stats)).ToList(),
            byUser
                .Select(x =>
                {
                    var (name, email) = users.TryGetValue(x.Key.UserId, out var user) ? user : (string.Empty, string.Empty);
                    return new AiUserUsageRow(
                        x.Key.UserId,
                        name,
                        email,
                        x.Key.TenantId,
                        tenantNames.GetValueOrDefault(x.Key.TenantId, string.Empty),
                        x.Key.ClinicId,
                        clinicNames.GetValueOrDefault(x.Key.ClinicId, string.Empty),
                        x.Stats);
                })
                .ToList());
    }

    private async Task<Dictionary<Guid, AiClinicCreditTotals>> CreditTotalsAsync(
        IReadOnlyCollection<Guid> clinicIds, DateTime monthStartUtc, DateTime monthEndUtc, CancellationToken ct)
    {
        if (clinicIds.Count == 0)
            return [];

        var ids = clinicIds.Distinct().ToList();
        var month = await _db.AiUsageRecords.AsNoTracking()
            .Where(r => ids.Contains(r.ClinicId) && r.CreatedAtUtc >= monthStartUtc && r.CreatedAtUtc < monthEndUtc)
            .GroupBy(r => r.ClinicId)
            .Select(g => new
            {
                ClinicId = g.Key,
                Tokens = g.Sum(x => (long)x.InputTokens + x.OutputTokens),
                CostUsd = g.Sum(x => x.CostUsd),
                Credits = g.Sum(x => x.Credits),
                FundCredits = g.Sum(x => x.FundCredits),
            })
            .ToDictionaryAsync(x => x.ClinicId, ct);

        // Fon devreder: bakiye tüm zamanların eklenen − harcanan farkıdır.
        var fundUsed = await _db.AiUsageRecords.AsNoTracking()
            .Where(r => ids.Contains(r.ClinicId) && r.FundCredits > 0)
            .GroupBy(r => r.ClinicId)
            .Select(g => new { ClinicId = g.Key, Credits = g.Sum(x => x.FundCredits) })
            .ToDictionaryAsync(x => x.ClinicId, x => x.Credits, ct);
        var granted = await _db.AiCreditGrants.AsNoTracking()
            .Where(g => ids.Contains(g.ClinicId))
            .GroupBy(g => g.ClinicId)
            .Select(g => new { ClinicId = g.Key, Credits = g.Sum(x => (decimal)x.Credits) })
            .ToDictionaryAsync(x => x.ClinicId, x => x.Credits, ct);

        var result = new Dictionary<Guid, AiClinicCreditTotals>();
        foreach (var id in ids)
        {
            var m = month.GetValueOrDefault(id);
            var fundGranted = granted.GetValueOrDefault(id);
            var fundSpent = fundUsed.GetValueOrDefault(id);
            if (m is null && fundGranted == 0 && fundSpent == 0)
                continue;

            result[id] = new AiClinicCreditTotals(
                m?.Tokens ?? 0, m?.CostUsd ?? 0m, m?.Credits ?? 0m, m?.FundCredits ?? 0m, fundGranted, fundSpent);
        }

        return result;
    }

    private static async Task<List<(TKey Key, AiUsageStats Stats)>> GroupAsync<TKey>(
        IQueryable<AiUsageRecord> query, Expression<Func<AiUsageRecord, TKey>> key, CancellationToken ct)
    {
        var rows = await query
            .GroupBy(key)
            .Select(g => new
            {
                g.Key,
                Requests = g.Count(),
                Failed = g.Count(x => x.Outcome != AiUsageOutcome.Succeeded),
                InputTokens = g.Sum(x => (long)x.InputTokens),
                OutputTokens = g.Sum(x => (long)x.OutputTokens),
                Credits = g.Sum(x => x.Credits),
                FundCredits = g.Sum(x => x.FundCredits),
                CostUsd = g.Sum(x => x.CostUsd),
            })
            .ToListAsync(ct);

        return rows
            .Select(r => (r.Key, new AiUsageStats(r.Requests, r.Failed, r.InputTokens, r.OutputTokens, r.Credits, r.FundCredits, r.CostUsd)))
            .ToList();
    }
}
