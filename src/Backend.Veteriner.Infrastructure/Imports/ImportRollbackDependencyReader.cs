using Backend.Veteriner.Application.Imports.Rollback;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Imports;

/// <summary>
/// Geri alma bağımlılık okuması (IMPORT_API_CONTRACT.md §9, §12.9). Hedef kayıtlara bağlı kayıtları tablo başına bir sorguyla
/// okur (kimlik kümesi tek JSON parametresi; sorgu sayısı satır sayısına bağlı değil, en çok ~35). İlgili tablolarda
/// veritabanı yabancı anahtarı olmadığından bağ yalnız bu okumayla tespit edilir; ücret ve ödemede iptal edilmiş kayıtlar
/// da bağ sayılır (sorgu süzgeci yok sayılır: finansal kayıt sahipsiz kalmasın). Partinin kendi randevuları ve devir ücreti
/// bağ sayılmaz; ayrı olgu olarak okunur (ADR-048). Komut tarafında birimin <see cref="AppDbContext"/>'i ve transaction'ı
/// kullanılır.
/// </summary>
public sealed class ImportRollbackDependencyReader : IImportRollbackDependencyReader
{
    /// <param name="batchId">Partinin kendi kayıtlarını bağdan ayırmak için.</param>
    private delegate IQueryable<Guid> DependencySource(AppDbContext db, Guid tenantId, Guid[] ids, Guid batchId);

    /// <summary>Hasta bağları: neden kodu → hedef hastaya bağlı kayıtların hasta kimlikleri (partinin randevuları hariç).</summary>
    private static readonly (string Reason, DependencySource Query)[] PetSources =
    [
        (ImportRollbackReasons.HasAppointments, (db, t, ids, b) => db.Appointments
            .Where(x => x.TenantId == t && ids.Contains(x.PetId) && x.ImportBatchId != b).Select(x => x.PetId)),
        (ImportRollbackReasons.HasVisits, (db, t, ids, _) => db.Visits.Where(x => x.TenantId == t && ids.Contains(x.PetId)).Select(x => x.PetId)),
        (ImportRollbackReasons.HasExaminations, (db, t, ids, _) => db.Examinations.Where(x => x.TenantId == t && ids.Contains(x.PetId)).Select(x => x.PetId)),
        (ImportRollbackReasons.HasTreatments, (db, t, ids, _) => db.Treatments.Where(x => x.TenantId == t && ids.Contains(x.PetId)).Select(x => x.PetId)),
        (ImportRollbackReasons.HasPrescriptions, (db, t, ids, _) => db.Prescriptions.Where(x => x.TenantId == t && ids.Contains(x.PetId)).Select(x => x.PetId)),
        (ImportRollbackReasons.HasLabResults, (db, t, ids, _) => db.LabResults.Where(x => x.TenantId == t && ids.Contains(x.PetId)).Select(x => x.PetId)),
        (ImportRollbackReasons.HasHospitalizations, (db, t, ids, _) => db.Hospitalizations.Where(x => x.TenantId == t && ids.Contains(x.PetId)).Select(x => x.PetId)),
        (ImportRollbackReasons.HasAdministrations, (db, t, ids, _) => db.Administrations.Where(x => x.TenantId == t && ids.Contains(x.PetId)).Select(x => x.PetId)),
        (ImportRollbackReasons.HasCareInstructions, (db, t, ids, _) => db.CareInstructions.Where(x => x.TenantId == t && ids.Contains(x.PetId)).Select(x => x.PetId)),
        (ImportRollbackReasons.HasChargeSuggestions, (db, t, ids, _) => db.ChargeSuggestions.Where(x => x.TenantId == t && ids.Contains(x.PetId)).Select(x => x.PetId)),
        (ImportRollbackReasons.HasCharges, (db, t, ids, _) => db.Charges.IgnoreQueryFilters(QueryFilterNames.Ignorable)
            .Where(x => x.TenantId == t && x.PetId != null && ids.Contains(x.PetId.Value)).Select(x => x.PetId!.Value)),
        (ImportRollbackReasons.HasPayments, (db, t, ids, _) => db.Payments.IgnoreQueryFilters(QueryFilterNames.Ignorable)
            .Where(x => x.TenantId == t && x.PetId != null && ids.Contains(x.PetId.Value)).Select(x => x.PetId!.Value)),
    ];

    /// <summary>Müşteri bağları: neden kodu → hedef müşteriye bağlı finansal kayıtların müşteri kimlikleri (partinin devri hariç).</summary>
    private static readonly (string Reason, DependencySource Query)[] ClientSources =
    [
        (ImportRollbackReasons.HasCharges, (db, t, ids, b) => db.Charges.IgnoreQueryFilters(QueryFilterNames.Ignorable)
            .Where(x => x.TenantId == t && ids.Contains(x.ClientId) && x.ImportBatchId != b).Select(x => x.ClientId)),
        (ImportRollbackReasons.HasPayments, (db, t, ids, _) => db.Payments.IgnoreQueryFilters(QueryFilterNames.Ignorable).Where(x => x.TenantId == t && ids.Contains(x.ClientId)).Select(x => x.ClientId)),
        (ImportRollbackReasons.HasSales, (db, t, ids, _) => db.Sales.Where(x => x.TenantId == t && ids.Contains(x.ClientId)).Select(x => x.ClientId)),
    ];

    /// <summary>Ürün bağları (ADR-048 T7): stok, satış, reçete, uygulama, ücret önerisi, muayene şablonu.</summary>
    private static readonly (string Reason, DependencySource Query)[] ProductSources =
    [
        (ImportRollbackReasons.HasStock, (db, t, ids, _) => db.ProductStocks.Where(x => x.TenantId == t && ids.Contains(x.ProductId)).Select(x => x.ProductId)),
        (ImportRollbackReasons.HasStock, (db, t, ids, _) => db.StockBatches.Where(x => x.TenantId == t && ids.Contains(x.ProductId)).Select(x => x.ProductId)),
        (ImportRollbackReasons.HasStock, (db, t, ids, _) => db.StockMovements.Where(x => x.TenantId == t && ids.Contains(x.ProductId)).Select(x => x.ProductId)),
        (ImportRollbackReasons.HasSales, (db, _, ids, _) => db.SaleLines.Where(x => ids.Contains(x.ProductId)).Select(x => x.ProductId)),
        (ImportRollbackReasons.HasPrescriptions, (db, t, ids, _) => db.Prescriptions.Where(x => x.TenantId == t)
            .SelectMany(x => x.Items).Where(i => i.ProductId != null && ids.Contains(i.ProductId.Value)).Select(i => i.ProductId!.Value)),
        (ImportRollbackReasons.HasAdministrations, (db, t, ids, _) => db.Administrations.Where(x => x.TenantId == t && ids.Contains(x.ProductId)).Select(x => x.ProductId)),
        (ImportRollbackReasons.HasChargeSuggestions, (db, t, ids, _) => db.ChargeSuggestions
            .Where(x => x.TenantId == t && x.ProductId != null && ids.Contains(x.ProductId.Value)).Select(x => x.ProductId!.Value)),
        (ImportRollbackReasons.HasExaminationTemplates, (db, _, ids, _) => db.ExaminationTemplateLines.Where(x => ids.Contains(x.ProductId)).Select(x => x.ProductId)),
    ];

    private readonly AppDbContext _db;

    public ImportRollbackDependencyReader(AppDbContext db) => _db = db;

    public async Task<ImportRollbackFacts> ReadAsync(Guid tenantId, ImportRollbackTargets targets, CancellationToken ct)
    {
        var batchId = targets.BatchId;
        var vaccinationIds = targets.VaccinationIds.ToArray();
        var vaccinations = vaccinationIds.Length == 0
            ? []
            : await _db.Vaccinations.AsNoTracking()
                .Where(v => v.TenantId == tenantId && vaccinationIds.Contains(v.Id))
                .Select(v => new
                {
                    v.Id,
                    v.PetId,
                    Examination = v.ExaminationId != null,
                    Administration = _db.Administrations.Any(a => a.TenantId == tenantId && a.VaccinationId == v.Id),
                    Modified = v.UpdatedAtUtc != null,
                })
                .ToListAsync(ct);

        var clinicalRecordIds = targets.ClinicalRecordIds.ToArray();
        var clinicalRecords = clinicalRecordIds.Length == 0
            ? new Dictionary<Guid, Guid>()
            : await _db.ImportedClinicalRecords.AsNoTracking()
                .Where(r => r.TenantId == tenantId && clinicalRecordIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.PetId, ct);

        // Randevu değişti: artık planlı değil ya da oluşturulduktan sonra değiştirildi (mutasyon sırası ilerledi).
        var appointmentIds = targets.AppointmentIds.ToArray();
        var appointments = appointmentIds.Length == 0
            ? []
            : await _db.Appointments.AsNoTracking()
                .Where(a => a.TenantId == tenantId && appointmentIds.Contains(a.Id))
                .Select(a => new
                {
                    a.Id,
                    a.PetId,
                    Changed = a.Status != AppointmentStatus.Scheduled || a.MutationSequence > 1,
                    Visits = _db.Visits.Any(v => v.TenantId == tenantId && v.AppointmentId == a.Id),
                    Examinations = _db.Examinations.Any(e => e.TenantId == tenantId && e.AppointmentId == a.Id),
                })
                .ToListAsync(ct);

        var petIds = targets.PetIds.ToArray();
        var ownerLookupIds = petIds
            .Concat(vaccinations.Select(v => v.PetId))
            .Concat(clinicalRecords.Values)
            .Concat(appointments.Select(a => a.PetId))
            .Distinct()
            .ToArray();
        var petOwners = ownerLookupIds.Length == 0
            ? new Dictionary<Guid, Guid>()
            : await _db.Pets.AsNoTracking()
                .Where(p => p.TenantId == tenantId && ownerLookupIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.ClientId, ct);

        var petVaccinations = petIds.Length == 0
            ? []
            : await _db.Vaccinations.AsNoTracking()
                .Where(v => v.TenantId == tenantId && petIds.Contains(v.PetId))
                .Select(v => new { v.PetId, v.Id })
                .ToListAsync(ct);

        var petClinicalRecords = petIds.Length == 0
            ? []
            : await _db.ImportedClinicalRecords.AsNoTracking()
                .Where(r => r.TenantId == tenantId && petIds.Contains(r.PetId))
                .Select(r => new { r.PetId, r.Id })
                .ToListAsync(ct);

        // Değişti = oluşturulduktan sonra güncellendi. Oluşturma anında yazılan fatura kimliği (içe aktarmada KimlikNo) aynı anı
        // taşır ve değişiklik sayılmaz.
        var clientIds = targets.ClientIds.ToArray();
        var clientModified = clientIds.Length == 0
            ? new Dictionary<Guid, bool>()
            : await _db.Clients.AsNoTracking()
                .Where(c => c.TenantId == tenantId && clientIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.UpdatedAtUtc != null && c.UpdatedAtUtc > c.CreatedAtUtc, ct);

        var clientPets = clientIds.Length == 0
            ? []
            : await _db.Pets.AsNoTracking()
                .Where(p => p.TenantId == tenantId && clientIds.Contains(p.ClientId))
                .Select(p => new { p.ClientId, p.Id })
                .ToListAsync(ct);

        var accountEntryIds = targets.AccountEntryIds.ToArray();
        var accountEntries = accountEntryIds.Length == 0
            ? new Dictionary<Guid, Guid>()
            : await _db.ImportedAccountEntries.AsNoTracking()
                .Where(e => e.TenantId == tenantId && accountEntryIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.ClientId, ct);

        var clientAccountEntries = clientIds.Length == 0
            ? []
            : await _db.ImportedAccountEntries.AsNoTracking()
                .Where(e => e.TenantId == tenantId && clientIds.Contains(e.ClientId))
                .Select(e => new { e.ClientId, e.Id })
                .ToListAsync(ct);

        // Partinin devir ücretleri (müşteri başına en çok bir); iptal ve tahsis "değişti" sayılır.
        var openingBalances = clientIds.Length == 0
            ? []
            : await _db.Charges.IgnoreQueryFilters(QueryFilterNames.Ignorable).AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.ImportBatchId == batchId && clientIds.Contains(c.ClientId))
                .Select(c => new { c.ClientId, c.Id, Changed = c.VoidedAtUtc != null || c.AllocatedAmount > 0 })
                .ToListAsync(ct);

        var productIds = targets.ProductIds.ToArray();
        var products = productIds.Length == 0
            ? new Dictionary<Guid, bool>()
            : await _db.Products.AsNoTracking()
                .Where(p => p.TenantId == tenantId && productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.UpdatedAtUtc != null, ct);
        var productBlockers = await ReadBlockersAsync(ProductSources, tenantId, products.Keys.ToArray(), batchId, ct);

        return new ImportRollbackFacts(
            vaccinations.ToDictionary(
                v => v.Id,
                v => new ImportRollbackVaccinationFact(v.PetId, v.Examination, v.Administration, v.Modified)),
            petOwners,
            Group(petVaccinations, x => x.PetId, x => x.Id),
            await ReadBlockersAsync(PetSources, tenantId, petIds, batchId, ct),
            clientModified,
            Group(clientPets, x => x.ClientId, x => x.Id),
            await ReadBlockersAsync(ClientSources, tenantId, clientIds, batchId, ct))
        {
            ClinicalRecords = clinicalRecords,
            PetClinicalRecords = Group(petClinicalRecords, x => x.PetId, x => x.Id),
            AccountEntries = accountEntries,
            ClientAccountEntries = Group(clientAccountEntries, x => x.ClientId, x => x.Id),
            Appointments = appointments.ToDictionary(
                a => a.Id,
                a => new ImportRollbackAppointmentFact(a.PetId, a.Changed, a.Visits, a.Examinations)),
            OpeningBalances = openingBalances
                .GroupBy(c => c.ClientId)
                .ToDictionary(g => g.Key, g => new ImportRollbackOpeningBalanceFact(g.First().Id, g.Any(c => c.Changed))),
            Products = products.ToDictionary(
                kv => kv.Key,
                kv =>
                {
                    var reasons = new HashSet<string>(productBlockers.GetValueOrDefault(kv.Key) ?? new HashSet<string>(), StringComparer.Ordinal);
                    if (kv.Value)
                        reasons.Add(ImportRollbackReasons.ProductModified);
                    return (IReadOnlySet<string>)reasons;
                }),
        };
    }

    private static Dictionary<Guid, IReadOnlyList<Guid>> Group<T>(IEnumerable<T> rows, Func<T, Guid> key, Func<T, Guid> value)
        => rows.GroupBy(key).ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(value).ToList());

    private async Task<IReadOnlyDictionary<Guid, IReadOnlySet<string>>> ReadBlockersAsync(
        (string Reason, DependencySource Query)[] sources, Guid tenantId, Guid[] ids, Guid batchId, CancellationToken ct)
    {
        var blockers = new Dictionary<Guid, HashSet<string>>();
        if (ids.Length == 0)
            return new Dictionary<Guid, IReadOnlySet<string>>();

        foreach (var (reason, query) in sources)
        {
            var linked = await query(_db, tenantId, ids, batchId).Distinct().ToListAsync(ct);
            foreach (var id in linked)
            {
                if (!blockers.TryGetValue(id, out var set))
                    blockers[id] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(reason);
            }
        }

        return blockers.ToDictionary(kv => kv.Key, kv => (IReadOnlySet<string>)kv.Value);
    }
}
