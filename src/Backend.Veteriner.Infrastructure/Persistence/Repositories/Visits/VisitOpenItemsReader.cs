using Backend.Veteriner.Application.Hbs;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.ReadModels;
using Backend.Veteriner.Domain.Hbs;
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
        // Tamamlanmış ve bilinçli ücretsiz kapatılmamış ziyaretler: işlemi olmasa da ücretsiz kalırsa "ücret yok" sayılır.
        var completedUnconfirmed = scope.Sees(OpenItemSources.Charges)
            ? (await _db.Visits.AsNoTracking()
                .Where(v => ids.Contains(v.Id) && v.CareStatus == VisitCareStatus.Completed && v.NoChargeConfirmedAtUtc == null)
                .Select(v => v.Id)
                .ToListAsync(ct)).ToHashSet()
            : [];
        var workCounts = scope.Visible == OpenItemSources.None
            ? new Dictionary<Guid, int>()
            : await WorkVisitIds(scope, ids)
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
                Classify(hasCharge, totals.Charged, totals.Allocated, work, completedUnconfirmed.Contains(id)),
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
            uncharged = await UnchargedVisits(request).CountAsync(ct);

            // Borçlu çıkan: tamamlanmış ziyaretin açık tutarı > 0. Yalnız açık ücretler (AllocatedAmount < Amount) toplanır:
            // tahsisi tamamlanmış ücret toplama 0 katar (CK_Charges_AllocatedAmount_Range), bu yüzden sonuç aynıdır ama
            // kliniğin tüm ücret geçmişi yerine küçük bir küme taranır (10k ölçekte tüm geçmiş 30 sn zaman aşımına giriyordu).
            var outstanding = await (
                    from c in ChargeFacts(scope)
                    where c.AllocatedAmount < c.Amount
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

        var incomplete = await IncompleteVisits(request).CountAsync(ct);

        // HBS'e girilmedi (ADR-036 Dilim 2): takip kapsamı HbsTracking'den; yalnız pencere verilmişse (Bugün).
        int? hbsPrescriptions = null;
        int? hbsVaccinations = null;
        if (request is { HbsFromUtc: { } hbsFrom, HbsToUtc: { } hbsTo })
        {
            if (scope.Sees(OpenItemSources.Prescriptions))
            {
                hbsPrescriptions = await _db.Prescriptions.AsNoTracking()
                    .Where(p => p.TenantId == scope.TenantId && p.ClinicId == scope.ClinicId
                                && p.PrescribedAtUtc >= hbsFrom && p.PrescribedAtUtc < hbsTo)
                    .Where(HbsTracking.PrescriptionWithStatus(HbsEntryStatus.NotEntered))
                    .CountAsync(ct);
            }

            if (scope.Sees(OpenItemSources.Vaccinations))
            {
                hbsVaccinations = await _db.Vaccinations.AsNoTracking()
                    .Where(v => v.TenantId == scope.TenantId && v.ClinicId == scope.ClinicId
                                && v.AppliedAtUtc >= hbsFrom && v.AppliedAtUtc < hbsTo)
                    .Where(HbsTracking.VaccinationTrackedWithStatus(HbsEntryStatus.NotEntered))
                    .CountAsync(ct);
            }
        }

        return new OpenItemsSummaryDto(uncharged, outstandingVisits, outstandingAmount, awaitingLabs, incomplete, hbsPrescriptions, hbsVaccinations);
    }

    public async Task<OpenItemVisitsReadResult> ListVisitsAsync(
        OpenItemsSummaryRequest request, OpenItemVisitKind kind, int maxItems, CancellationToken ct = default)
    {
        var visits = kind switch
        {
            OpenItemVisitKind.Uncharged => UnchargedVisits(request),
            OpenItemVisitKind.Incomplete => IncompleteVisits(request),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        var total = await visits.CountAsync(ct);
        if (total == 0)
            return new OpenItemVisitsReadResult(0, []);

        var examinations = _db.Examinations.AsNoTracking()
            .Where(e => e.TenantId == request.Scope.TenantId && e.ClinicId == request.Scope.ClinicId);
        var rows = await (
                from v in visits
                join p in _db.Pets.AsNoTracking() on v.PetId equals p.Id
                join c in _db.Clients.AsNoTracking() on p.ClientId equals c.Id
                orderby v.ArrivedAtUtc, v.Id
                select new OpenItemVisitRow(
                    v.Id,
                    v.ArrivedAtUtc,
                    v.CareStatus,
                    p.Id,
                    p.Name,
                    p.Species != null ? p.Species!.Name : null,
                    c.Id,
                    c.FullName,
                    v.ResponsibleVeterinarianUserId,
                    examinations
                        .Where(e => e.VisitId == v.Id)
                        .OrderByDescending(e => e.ExaminedAtUtc)
                        .ThenByDescending(e => e.Id)
                        .Select(e => (Guid?)e.Id)
                        .FirstOrDefault()))
            .Take(maxItems)
            .ToListAsync(ct);

        return new OpenItemVisitsReadResult(total, rows);
    }

    /// <summary>
    /// "Ücret yok" ziyaretleri (madde 1): pencere içinde gelmiş, ziyarete bağlı hiç ücreti olmayan ve ya görünür ücretlendirilebilir
    /// işlemi olan ya da tamamlanmış ama bilinçli ücretsiz kapatılmamış (madde 1a, 2026-11-19). Özet sayısı ve liste bu tek parçadan beslenir.
    /// </summary>
    private IQueryable<Visit> UnchargedVisits(OpenItemsSummaryRequest request)
    {
        var scope = request.Scope;
        var work = WorkVisitIds(scope);
        var charged = ChargeFacts(scope).Select(c => c.VisitId);
        return OpenVisits(scope)
            .Where(v => v.ArrivedAtUtc >= request.UnchargedFromUtc
                        && v.ArrivedAtUtc < request.UnchargedToUtc
                        && (work.Contains(v.Id)
                            || (v.CareStatus == VisitCareStatus.Completed && v.NoChargeConfirmedAtUtc == null))
                        && !charged.Contains(v.Id));
    }

    /// <summary>Tamamlanmamış geliş (madde 1): <c>IncompleteBeforeUtc</c>'den önce gelip tamamlanmamış, yanlış işaretsiz ziyaretler.</summary>
    private IQueryable<Visit> IncompleteVisits(OpenItemsSummaryRequest request)
        => OpenVisits(request.Scope)
            .Where(v => v.CareStatus != VisitCareStatus.Completed && v.ArrivedAtUtc < request.IncompleteBeforeUtc);

    /// <summary>
    /// Ücret varsa açık tutara, yoksa işlemin varlığına göre ziyaret ücret durumu (tek yer). Tamamlanmış ve bilinçli ücretsiz kapatılmamış
    /// ziyaret işlemi olmasa da <see cref="VisitChargeIndicator.ChargeMissing"/> sayılır.
    /// </summary>
    private static VisitChargeIndicator Classify(bool hasCharge, decimal charged, decimal allocated, int workCount, bool completedUnconfirmed)
    {
        if (!hasCharge)
            return workCount > 0 || completedUnconfirmed ? VisitChargeIndicator.ChargeMissing : VisitChargeIndicator.NoChargeableWork;

        if (charged - allocated <= 0)
            return VisitChargeIndicator.Paid;

        return allocated > 0 ? VisitChargeIndicator.PartiallyPaid : VisitChargeIndicator.PaymentPending;
    }

    private IQueryable<Visit> OpenVisits(OpenItemsScope scope)
        => _db.Visits.AsNoTracking()
            .Where(v => v.TenantId == scope.TenantId && v.ClinicId == scope.ClinicId && v.VoidedAtUtc == null);

    /// <summary>
    /// İptal edilmemiş (global filtre) ücretler; ziyaret bağı <c>VisitId ?? muayenenin VisitId'si</c>. Ziyarete bağlanamayanlar dışarıda.
    /// Muayene bağı sol birleşimle kurulur; ücret başına ilişkili alt sorgu (üç kez yineleniyordu) sorguyu ücret sayısıyla
    /// çarpıyor ve 10k ölçekte zaman aşımına götürüyordu.
    /// </summary>
    private IQueryable<ChargeFact> ChargeFacts(OpenItemsScope scope)
        => from c in _db.Charges.AsNoTracking()
           where c.TenantId == scope.TenantId && c.ClinicId == scope.ClinicId
           join e in _db.Examinations.AsNoTracking() on c.ExaminationId equals e.Id into examinations
           from e in examinations.DefaultIfEmpty()
           let visitId = c.VisitId ?? (e != null ? e.VisitId : null)
           where visitId != null
           select new ChargeFact { VisitId = visitId!.Value, Amount = c.Amount, AllocatedAmount = c.AllocatedAmount };

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
    /// <paramref name="visitIds"/> verilirse süzgeç her dala ayrı uygulanır: birleşimden sonra süzmek kliniğin tüm yılını
    /// birleştiriyordu (10k ölçekte ~300 ms); dal başına süzgeç ilgili indeksleri kullanır.
    /// </summary>
    private IQueryable<Guid> WorkVisitIds(OpenItemsScope scope, IReadOnlyCollection<Guid>? visitIds = null)
    {
        var tenantId = scope.TenantId;
        var clinicId = scope.ClinicId;
        var ids = visitIds?.ToList();
        // Boş taban: türlerin hiçbiri görünür değilse sorgu yine geçerlidir.
        var result = _db.Visits.Where(_ => false).Select(v => v.Id);

        // Muayene üzerinden ziyarete bağlanan kayıtlar için ortak kaynak: kapsamdaki, ziyaretli (ve istenirse seçili ziyaretlerdeki) muayeneler.
        var examinations = _db.Examinations.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.ClinicId == clinicId && e.VisitId != null);
        if (ids is not null)
            examinations = examinations.Where(e => ids.Contains(e.VisitId!.Value));

        if (scope.Sees(OpenItemSources.Examinations))
            result = result.Concat(examinations.Select(e => e.VisitId!.Value));

        if (scope.Sees(OpenItemSources.Treatments))
        {
            result = result.Concat(
                from t in _db.Treatments.AsNoTracking()
                join e in examinations on t.ExaminationId equals e.Id
                where t.TenantId == tenantId && t.ClinicId == clinicId && t.CancelledAtUtc == null
                select e.VisitId!.Value);
        }

        if (scope.Sees(OpenItemSources.Administrations))
        {
            result = result.Concat(
                from a in _db.Administrations.AsNoTracking()
                join e in examinations on a.ExaminationId equals e.Id
                where a.TenantId == tenantId && a.ClinicId == clinicId && a.VoidedAtUtc == null
                select e.VisitId!.Value);
        }

        if (scope.Sees(OpenItemSources.Vaccinations))
        {
            result = result.Concat(
                from v in _db.Vaccinations.AsNoTracking()
                join e in examinations on v.ExaminationId equals e.Id
                where v.TenantId == tenantId && v.ClinicId == clinicId
                      && v.Status == VaccinationStatus.Applied
                      // Stoklu aşı tek girişte uygulama da üretir; uygulama sayılırsa aşı ayrıca sayılmaz.
                      && !_db.Administrations.Any(a => a.VaccinationId == v.Id && a.VoidedAtUtc == null)
                select e.VisitId!.Value);
        }

        if (scope.Sees(OpenItemSources.Hospitalizations))
        {
            var hospitalizations =
                from h in _db.Hospitalizations.AsNoTracking()
                where h.TenantId == tenantId && h.ClinicId == clinicId && h.CancelledAtUtc == null
                join e in _db.Examinations.AsNoTracking() on h.ExaminationId equals e.Id into hospitalizationExaminations
                from e in hospitalizationExaminations.DefaultIfEmpty()
                let visitId = h.VisitId ?? (e != null ? e.VisitId : null)
                where visitId != null
                select visitId!.Value;
            if (ids is not null)
                hospitalizations = hospitalizations.Where(id => ids.Contains(id));
            result = result.Concat(hospitalizations);
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
