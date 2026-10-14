using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.ReadModels;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Domain.Visits;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Visits;

/// <summary>
/// Açık iş göstergelerinin tek hesap yeri (komut veritabanı). Ziyaret–kayıt bağı: kaydın kendi <c>VisitId</c>'si,
/// yoksa bağlı muayenenin <c>VisitId</c>'si. Ücretlendirilebilir işlem kapsamı ve "ücret yok" kuralı yalnızca burada tanımlıdır
/// (docs/OPEN_ITEMS_API_CONTRACT.md madde 1-2). Satır ve özet aynı kaynak sorgu parçalarından beslenir.
/// </summary>
public sealed class VisitOpenItemsReader : IVisitOpenItemsReader
{
    private readonly AppDbContext _db;

    public VisitOpenItemsReader(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyDictionary<Guid, VisitOpenItems>> GetForVisitsAsync(
        OpenItemsScope scope, IReadOnlyCollection<Guid> visitIds, CancellationToken ct = default)
    {
        if (visitIds.Count == 0)
            return new Dictionary<Guid, VisitOpenItems>();

        var ids = visitIds.ToList();
        var workCounts = scope.Visible == OpenItemSources.None
            ? new Dictionary<Guid, int>()
            : await WorkVisitIds(scope)
                .Where(id => ids.Contains(id))
                .GroupBy(id => id)
                .Select(g => new { VisitId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.VisitId, x => x.Count, ct);

        var charges = scope.Sees(OpenItemSources.Charges)
            ? await ChargeFacts(scope)
                .Where(c => ids.Contains(c.VisitId))
                .GroupBy(c => c.VisitId)
                .Select(g => new { VisitId = g.Key, Charged = g.Sum(c => c.Amount), Allocated = g.Sum(c => c.AllocatedAmount) })
                .ToDictionaryAsync(x => x.VisitId, x => (x.Charged, x.Allocated), ct)
            : null;

        var labs = scope.Sees(OpenItemSources.LabResults)
            ? await AwaitingLabVisitIds(scope)
                .Where(id => ids.Contains(id))
                .GroupBy(id => id)
                .Select(g => new { VisitId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.VisitId, x => x.Count, ct)
            : null;

        return ids.Distinct().ToDictionary(id => id, id =>
        {
            var work = workCounts.GetValueOrDefault(id);
            if (charges is null)
                return new VisitOpenItems(null, null, null, null, labs?.GetValueOrDefault(id));

            var hasCharge = charges.TryGetValue(id, out var totals);
            return new VisitOpenItems(
                Classify(hasCharge, totals.Charged, totals.Allocated, work),
                work,
                totals.Charged,
                totals.Allocated,
                labs?.GetValueOrDefault(id));
        });
    }

    public async Task<OpenItemsSummaryDto> GetSummaryAsync(OpenItemsSummaryRequest request, CancellationToken ct = default)
    {
        var scope = request.Scope;
        int? uncharged = null;
        int? outstandingVisits = null;
        decimal? outstandingAmount = null;
        int? awaitingLabs = null;

        if (scope.Sees(OpenItemSources.Charges))
        {
            var work = WorkVisitIds(scope);
            var charged = ChargeFacts(scope).Select(c => c.VisitId);
            uncharged = await OpenVisits(scope)
                .Where(v => v.ArrivedAtUtc >= request.UnchargedFromUtc
                            && v.ArrivedAtUtc < request.UnchargedToUtc
                            && work.Contains(v.Id)
                            && !charged.Contains(v.Id))
                .CountAsync(ct);

            // Borçlu çıkan: tamamlanmış ziyaretin açık tutarı > 0.
            var outstanding = await (
                    from c in ChargeFacts(scope)
                    join v in OpenVisits(scope) on c.VisitId equals v.Id
                    where v.CareStatus == VisitCareStatus.Completed
                    group c by c.VisitId into g
                    select g.Sum(c => c.Amount - c.AllocatedAmount))
                .Where(open => open > 0)
                .ToListAsync(ct);
            outstandingVisits = outstanding.Count;
            outstandingAmount = outstanding.Sum();
        }

        if (scope.Sees(OpenItemSources.LabResults))
        {
            awaitingLabs = await _db.LabResults.AsNoTracking()
                .CountAsync(l => l.TenantId == scope.TenantId
                                 && l.ClinicId == scope.ClinicId
                                 && l.Status == LabResultStatus.Requested, ct);
        }

        var incomplete = await OpenVisits(scope)
            .CountAsync(v => v.CareStatus != VisitCareStatus.Completed && v.ArrivedAtUtc < request.IncompleteBeforeUtc, ct);

        return new OpenItemsSummaryDto(uncharged, outstandingVisits, outstandingAmount, awaitingLabs, incomplete);
    }

    /// <summary>Ücret varsa açık tutara, yoksa işlemin varlığına göre ziyaret ücret durumu (tek yer).</summary>
    private static VisitChargeIndicator Classify(bool hasCharge, decimal charged, decimal allocated, int workCount)
    {
        if (!hasCharge)
            return workCount > 0 ? VisitChargeIndicator.ChargeMissing : VisitChargeIndicator.NoChargeableWork;

        if (charged - allocated <= 0)
            return VisitChargeIndicator.Paid;

        return allocated > 0 ? VisitChargeIndicator.PartiallyPaid : VisitChargeIndicator.PaymentPending;
    }

    private IQueryable<Visit> OpenVisits(OpenItemsScope scope)
        => _db.Visits.AsNoTracking()
            .Where(v => v.TenantId == scope.TenantId && v.ClinicId == scope.ClinicId && v.VoidedAtUtc == null);

    /// <summary>İptal edilmemiş (global filtre) ücretler; ziyaret bağı <c>VisitId ?? muayenenin VisitId'si</c>. Ziyarete bağlanamayanlar dışarıda.</summary>
    private IQueryable<ChargeFact> ChargeFacts(OpenItemsScope scope)
        => _db.Charges.AsNoTracking()
            .Where(c => c.TenantId == scope.TenantId && c.ClinicId == scope.ClinicId)
            .Select(c => new
            {
                VisitId = c.VisitId ?? _db.Examinations.Where(e => e.Id == c.ExaminationId).Select(e => e.VisitId).FirstOrDefault(),
                c.Amount,
                c.AllocatedAmount,
            })
            .Where(c => c.VisitId != null)
            .Select(c => new ChargeFact { VisitId = c.VisitId!.Value, Amount = c.Amount, AllocatedAmount = c.AllocatedAmount });

    /// <summary>Sonuç girilmemiş (<c>Requested</c>) lab kayıtlarının ziyaret kimlikleri (kayıt başına bir satır).</summary>
    private IQueryable<Guid> AwaitingLabVisitIds(OpenItemsScope scope)
        => _db.LabResults.AsNoTracking()
            .Where(l => l.TenantId == scope.TenantId && l.ClinicId == scope.ClinicId && l.Status == LabResultStatus.Requested)
            .Select(l => l.VisitId ?? _db.Examinations.Where(e => e.Id == l.ExaminationId).Select(e => e.VisitId).FirstOrDefault())
            .Where(id => id != null)
            .Select(id => id!.Value);

    /// <summary>
    /// Ücretlendirilebilir işlemlerin ziyaret kimlikleri (işlem başına bir satır); yalnızca kullanıcının görebildiği türler.
    /// Kapsam: muayene, tedavi, iptal edilmemiş uygulama, uygulanmış aşı (bağlı uygulaması yoksa), yatış.
    /// </summary>
    private IQueryable<Guid> WorkVisitIds(OpenItemsScope scope)
    {
        var tenantId = scope.TenantId;
        var clinicId = scope.ClinicId;
        // Boş taban: türlerin hiçbiri görünür değilse sorgu yine geçerlidir.
        var result = _db.Visits.Where(_ => false).Select(v => v.Id);

        if (scope.Sees(OpenItemSources.Examinations))
        {
            result = result.Concat(_db.Examinations.AsNoTracking()
                .Where(e => e.TenantId == tenantId && e.ClinicId == clinicId && e.VisitId != null)
                .Select(e => e.VisitId!.Value));
        }

        if (scope.Sees(OpenItemSources.Treatments))
        {
            result = result.Concat(
                from t in _db.Treatments.AsNoTracking()
                join e in _db.Examinations.AsNoTracking() on t.ExaminationId equals e.Id
                where t.TenantId == tenantId && t.ClinicId == clinicId && e.VisitId != null
                select e.VisitId!.Value);
        }

        if (scope.Sees(OpenItemSources.Administrations))
        {
            result = result.Concat(
                from a in _db.Administrations.AsNoTracking()
                join e in _db.Examinations.AsNoTracking() on a.ExaminationId equals e.Id
                where a.TenantId == tenantId && a.ClinicId == clinicId && a.VoidedAtUtc == null && e.VisitId != null
                select e.VisitId!.Value);
        }

        if (scope.Sees(OpenItemSources.Vaccinations))
        {
            result = result.Concat(
                from v in _db.Vaccinations.AsNoTracking()
                join e in _db.Examinations.AsNoTracking() on v.ExaminationId equals e.Id
                where v.TenantId == tenantId && v.ClinicId == clinicId
                      && v.Status == VaccinationStatus.Applied
                      && e.VisitId != null
                      // Stoklu aşı tek girişte uygulama da üretir; uygulama sayılırsa aşı ayrıca sayılmaz.
                      && !_db.Administrations.Any(a => a.VaccinationId == v.Id && a.VoidedAtUtc == null)
                select e.VisitId!.Value);
        }

        if (scope.Sees(OpenItemSources.Hospitalizations))
        {
            result = result.Concat(_db.Hospitalizations.AsNoTracking()
                .Where(h => h.TenantId == tenantId && h.ClinicId == clinicId)
                .Select(h => h.VisitId ?? _db.Examinations.Where(e => e.Id == h.ExaminationId).Select(e => e.VisitId).FirstOrDefault())
                .Where(id => id != null)
                .Select(id => id!.Value));
        }

        return result;
    }

    private sealed class ChargeFact
    {
        public Guid VisitId { get; init; }
        public decimal Amount { get; init; }
        public decimal AllocatedAmount { get; init; }
    }
}
