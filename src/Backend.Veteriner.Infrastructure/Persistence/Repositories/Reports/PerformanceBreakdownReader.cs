using Backend.Veteriner.Application.Reports.DailyTransactions;
using Backend.Veteriner.Application.Reports.Performance.Breakdowns;
using Backend.Veteriner.Application.Users.Common;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Reports;

/// <summary>
/// TIER-004 kırılım okuyucusu. Ücret olayları <see cref="FinanceEvents"/>'ten (gün sonu dökümünün kaynağı) gelir: oluşum +, dönemde iptal −.
/// Ziyaret ve muayene iptal edilmiş olsa da hekim ataması değişmez (global süzgeç atlanır); muayene sayımı muayene raporuyla aynı aralıktır.
/// </summary>
public sealed class PerformanceBreakdownReader : IPerformanceBreakdownReader
{
    private static readonly DailyTransactionKind[] ChargeKinds = [DailyTransactionKind.Charge, DailyTransactionKind.ChargeVoid];

    private readonly AppDbContext _db;

    public PerformanceBreakdownReader(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ChargeBreakdownRow>> ReadChargesAsync(FinanceEventFilter filter, CancellationToken ct = default)
    {
        var visits = _db.Visits.IgnoreQueryFilters(QueryFilterNames.Ignorable).AsNoTracking();
        var examinations = _db.Examinations.IgnoreQueryFilters(QueryFilterNames.Ignorable).AsNoTracking();
        var pets = _db.Pets.IgnoreQueryFilters(QueryFilterNames.Ignorable).AsNoTracking();

        var rows = FinanceEvents.Query(_db, filter, ChargeKinds)
            .Join(_db.Charges.IgnoreQueryFilters(QueryFilterNames.Ignorable).AsNoTracking(), e => e.ReferenceId, c => c.Id, (e, c) => new
            {
                Created = e.Kind == DailyTransactionKind.Charge,
                Amount = e.Amount ?? 0m,
                c.LineKind,
                c.Description,
                VetId = c.VisitId != null
                    ? visits.Where(v => v.Id == c.VisitId).Select(v => v.ResponsibleVeterinarianUserId).FirstOrDefault()
                    : c.ExaminationId != null
                        ? examinations.Where(x => x.Id == c.ExaminationId)
                            .Select(x => x.VisitId != null
                                ? visits.Where(v => v.Id == x.VisitId).Select(v => v.ResponsibleVeterinarianUserId).FirstOrDefault()
                                : x.FinalizedByUserId)
                            .FirstOrDefault()
                        : null,
                SpeciesId = c.PetId != null ? pets.Where(p => p.Id == c.PetId).Select(p => (Guid?)p.SpeciesId).FirstOrDefault() : null,
            });

        var groups = await rows
            .GroupBy(r => new { r.VetId, r.LineKind, r.SpeciesId, r.Description })
            .Select(g => new
            {
                g.Key.VetId,
                g.Key.LineKind,
                g.Key.SpeciesId,
                g.Key.Description,
                Net = g.Sum(r => r.Created ? r.Amount : -r.Amount),
                Count = g.Count(r => r.Created),
            })
            .ToListAsync(ct);

        return groups.Select(g => new ChargeBreakdownRow(g.VetId, g.LineKind, g.SpeciesId, g.Description, g.Net, g.Count)).ToList();
    }

    public async Task<IReadOnlyList<ExaminationBreakdownRow>> ReadExaminationsAsync(FinanceEventFilter filter, CancellationToken ct = default)
    {
        var visits = _db.Visits.IgnoreQueryFilters(QueryFilterNames.Ignorable).AsNoTracking();
        var fromUtc = filter.FromUtc;
        var toUtc = filter.ToUtcExclusive;

        var groups = await InScope(_db.Examinations.AsNoTracking(), filter)
            .Where(x => x.ExaminedAtUtc >= fromUtc && x.ExaminedAtUtc < toUtc)
            .Select(x => new
            {
                VetId = x.VisitId != null
                    ? visits.Where(v => v.Id == x.VisitId).Select(v => v.ResponsibleVeterinarianUserId).FirstOrDefault()
                    : x.FinalizedByUserId,
                SpeciesId = _db.Pets.IgnoreQueryFilters(QueryFilterNames.Ignorable).Where(p => p.Id == x.PetId).Select(p => (Guid?)p.SpeciesId).FirstOrDefault(),
            })
            .GroupBy(r => new { r.VetId, r.SpeciesId })
            .Select(g => new { g.Key.VetId, g.Key.SpeciesId, Count = g.Count() })
            .ToListAsync(ct);

        return groups.Select(g => new ExaminationBreakdownRow(g.VetId, g.SpeciesId, g.Count)).ToList();
    }

    public async Task<PerformanceActivityDto> ReadActivityAsync(FinanceEventFilter filter, CancellationToken ct = default)
    {
        var fromUtc = filter.FromUtc;
        var toUtc = filter.ToUtcExclusive;
        var visits = InScope(_db.Visits.AsNoTracking(), filter).Where(v => v.VoidedAtUtc == null && v.ArrivedAtUtc >= fromUtc && v.ArrivedAtUtc < toUtc);

        var visitCount = await visits.CountAsync(ct);
        var clientCount = await visits
            .Join(_db.Pets.IgnoreQueryFilters(QueryFilterNames.Ignorable), v => v.PetId, p => p.Id, (v, p) => p.ClientId)
            .Distinct()
            .CountAsync(ct);

        return new PerformanceActivityDto(visitCount, clientCount);
    }

    public async Task<IReadOnlyDictionary<Guid, string?>> LoadUserNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
        => userIds.Count == 0
            ? new Dictionary<Guid, string?>()
            : await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.DisplayName, u.Email })
                .ToDictionaryAsync(u => u.Id, u => UserDisplayName.Resolve(u.DisplayName, u.Email), ct);

    public async Task<IReadOnlyDictionary<Guid, string>> LoadSpeciesNamesAsync(IReadOnlyCollection<Guid> speciesIds, CancellationToken ct = default)
        => speciesIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Species.AsNoTracking().Where(s => speciesIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);

    private static IQueryable<T> InScope<T>(IQueryable<T> q, FinanceEventFilter f) where T : class
    {
        var tenantId = f.TenantId;
        q = q.Where(x => EF.Property<Guid>(x, "TenantId") == tenantId);
        if (f.ClinicId is { } clinicId)
            return q.Where(x => EF.Property<Guid>(x, "ClinicId") == clinicId);
        if (f.AccessibleClinicIds is { } accessible)
        {
            var ids = accessible.ToList();
            return ids.Count == 0 ? q.Where(_ => false) : q.Where(x => ids.Contains(EF.Property<Guid>(x, "ClinicId")));
        }

        return q;
    }
}
