using Backend.Veteriner.Application.Imports.Rollback;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Imports;

/// <summary>
/// Geri alma bağımlılık okuması (IMPORT_API_CONTRACT.md §9). Hedef kayıtlara bağlı kayıtları tablo başına bir sorguyla
/// okur (kimlik kümesi tek JSON parametresi; sorgu sayısı satır sayısına bağlı değil, en çok ~20). İlgili tablolarda
/// veritabanı yabancı anahtarı olmadığından bağ yalnız bu okumayla tespit edilir; ücret ve ödemede iptal edilmiş kayıtlar
/// da bağ sayılır (sorgu süzgeci yok sayılır: finansal kayıt sahipsiz kalmasın). Komut tarafında birimin
/// <see cref="AppDbContext"/>'i ve transaction'ı kullanılır.
/// </summary>
public sealed class ImportRollbackDependencyReader : IImportRollbackDependencyReader
{
    private delegate IQueryable<Guid> DependencySource(AppDbContext db, Guid tenantId, Guid[] ids);

    /// <summary>Hasta bağları: neden kodu → hedef hastaya bağlı kayıtların hasta kimlikleri.</summary>
    private static readonly (string Reason, DependencySource Query)[] PetSources =
    [
        (ImportRollbackReasons.HasAppointments, (db, t, ids) => db.Appointments.Where(x => x.TenantId == t && ids.Contains(x.PetId)).Select(x => x.PetId)),
        (ImportRollbackReasons.HasVisits, (db, t, ids) => db.Visits.Where(x => x.TenantId == t && ids.Contains(x.PetId)).Select(x => x.PetId)),
        (ImportRollbackReasons.HasExaminations, (db, t, ids) => db.Examinations.Where(x => x.TenantId == t && ids.Contains(x.PetId)).Select(x => x.PetId)),
        (ImportRollbackReasons.HasTreatments, (db, t, ids) => db.Treatments.Where(x => x.TenantId == t && ids.Contains(x.PetId)).Select(x => x.PetId)),
        (ImportRollbackReasons.HasPrescriptions, (db, t, ids) => db.Prescriptions.Where(x => x.TenantId == t && ids.Contains(x.PetId)).Select(x => x.PetId)),
        (ImportRollbackReasons.HasLabResults, (db, t, ids) => db.LabResults.Where(x => x.TenantId == t && ids.Contains(x.PetId)).Select(x => x.PetId)),
        (ImportRollbackReasons.HasHospitalizations, (db, t, ids) => db.Hospitalizations.Where(x => x.TenantId == t && ids.Contains(x.PetId)).Select(x => x.PetId)),
        (ImportRollbackReasons.HasAdministrations, (db, t, ids) => db.Administrations.Where(x => x.TenantId == t && ids.Contains(x.PetId)).Select(x => x.PetId)),
        (ImportRollbackReasons.HasCareInstructions, (db, t, ids) => db.CareInstructions.Where(x => x.TenantId == t && ids.Contains(x.PetId)).Select(x => x.PetId)),
        (ImportRollbackReasons.HasChargeSuggestions, (db, t, ids) => db.ChargeSuggestions.Where(x => x.TenantId == t && ids.Contains(x.PetId)).Select(x => x.PetId)),
        (ImportRollbackReasons.HasCharges, (db, t, ids) => db.Charges.IgnoreQueryFilters(QueryFilterNames.Ignorable)
            .Where(x => x.TenantId == t && x.PetId != null && ids.Contains(x.PetId.Value)).Select(x => x.PetId!.Value)),
        (ImportRollbackReasons.HasPayments, (db, t, ids) => db.Payments.IgnoreQueryFilters(QueryFilterNames.Ignorable)
            .Where(x => x.TenantId == t && x.PetId != null && ids.Contains(x.PetId.Value)).Select(x => x.PetId!.Value)),
    ];

    /// <summary>Müşteri bağları: neden kodu → hedef müşteriye bağlı finansal kayıtların müşteri kimlikleri.</summary>
    private static readonly (string Reason, DependencySource Query)[] ClientSources =
    [
        (ImportRollbackReasons.HasCharges, (db, t, ids) => db.Charges.IgnoreQueryFilters(QueryFilterNames.Ignorable).Where(x => x.TenantId == t && ids.Contains(x.ClientId)).Select(x => x.ClientId)),
        (ImportRollbackReasons.HasPayments, (db, t, ids) => db.Payments.IgnoreQueryFilters(QueryFilterNames.Ignorable).Where(x => x.TenantId == t && ids.Contains(x.ClientId)).Select(x => x.ClientId)),
        (ImportRollbackReasons.HasSales, (db, t, ids) => db.Sales.Where(x => x.TenantId == t && ids.Contains(x.ClientId)).Select(x => x.ClientId)),
    ];

    private readonly AppDbContext _db;

    public ImportRollbackDependencyReader(AppDbContext db) => _db = db;

    public async Task<ImportRollbackFacts> ReadAsync(Guid tenantId, ImportRollbackTargets targets, CancellationToken ct)
    {
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

        var petIds = targets.PetIds.ToArray();
        var ownerLookupIds = petIds.Concat(vaccinations.Select(v => v.PetId)).Distinct().ToArray();
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

        var clientIds = targets.ClientIds.ToArray();
        var clientModified = clientIds.Length == 0
            ? new Dictionary<Guid, bool>()
            : await _db.Clients.AsNoTracking()
                .Where(c => c.TenantId == tenantId && clientIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.UpdatedAtUtc != null, ct);

        var clientPets = clientIds.Length == 0
            ? []
            : await _db.Pets.AsNoTracking()
                .Where(p => p.TenantId == tenantId && clientIds.Contains(p.ClientId))
                .Select(p => new { p.ClientId, p.Id })
                .ToListAsync(ct);

        return new ImportRollbackFacts(
            vaccinations.ToDictionary(
                v => v.Id,
                v => new ImportRollbackVaccinationFact(v.PetId, v.Examination, v.Administration, v.Modified)),
            petOwners,
            petVaccinations.GroupBy(x => x.PetId).ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(x => x.Id).ToList()),
            await ReadBlockersAsync(PetSources, tenantId, petIds, ct),
            clientModified,
            clientPets.GroupBy(x => x.ClientId).ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(x => x.Id).ToList()),
            await ReadBlockersAsync(ClientSources, tenantId, clientIds, ct));
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlySet<string>>> ReadBlockersAsync(
        (string Reason, DependencySource Query)[] sources, Guid tenantId, Guid[] ids, CancellationToken ct)
    {
        var blockers = new Dictionary<Guid, HashSet<string>>();
        if (ids.Length == 0)
            return new Dictionary<Guid, IReadOnlySet<string>>();

        foreach (var (reason, query) in sources)
        {
            var linked = await query(_db, tenantId, ids).Distinct().ToListAsync(ct);
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
