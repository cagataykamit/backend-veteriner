using Backend.Veteriner.Application.Hbs;
using Backend.Veteriner.Application.Users.Common;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Hbs;

/// <summary>
/// HBS hazır verisinin kaynağı (komut veritabanı, yalnız verilen kiracı). Hekim: bağlı muayenenin geliş kaydındaki sorumlu hekim
/// (defter kuralı); aşının parti/SKT/dozu bağlı iptal edilmemiş uygulamadan. Sözleşme: HBS_ASSISTANT_API_CONTRACT.md bölüm 4.
/// </summary>
public sealed class HbsSourceReader : IHbsSourceReader
{
    private readonly AppDbContext _db;

    public HbsSourceReader(AppDbContext db) => _db = db;

    public async Task<HbsPrescriptionSource?> GetPrescriptionAsync(Guid tenantId, Guid prescriptionId, CancellationToken ct = default)
    {
        var p = await _db.Prescriptions.AsNoTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == prescriptionId, ct);
        if (p is null)
            return null;

        var (animal, owner) = await AnimalAndOwnerAsync(tenantId, p.PetId, ct);
        var veterinarianId = await ResponsibleVeterinarianIdAsync(tenantId, p.ExaminationId, ct);
        var names = await NamesAsync([veterinarianId, p.HbsMarkedByUserId], ct);

        return new HbsPrescriptionSource(
            p.Id,
            p.ClinicId,
            p.PrescribedAtUtc,
            p.Content,
            p.Items.OrderBy(i => i.SortOrder)
                .Select(i => new HbsPrescriptionItemSource(i.Name, i.Quantity, i.Unit, i.Dose, i.Instructions, i.LotNumber))
                .ToList(),
            animal,
            owner,
            Name(names, veterinarianId),
            new HbsEntrySource(p.HbsStatus, p.HbsReferenceNumber, p.HbsNotRequiredReason, p.HbsMarkedAtUtc, p.HbsMarkedByUserId,
                Name(names, p.HbsMarkedByUserId)));
    }

    public async Task<HbsVaccinationSource?> GetVaccinationAsync(Guid tenantId, Guid vaccinationId, CancellationToken ct = default)
    {
        var v = await _db.Vaccinations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == vaccinationId, ct);
        if (v is null)
            return null;

        var administration = await (
                from a in _db.Administrations.AsNoTracking()
                join product in _db.Products.AsNoTracking() on a.ProductId equals product.Id
                join b in _db.StockBatches.AsNoTracking() on a.BatchId equals b.Id into batches
                from b in batches.DefaultIfEmpty()
                where a.TenantId == tenantId && a.VaccinationId == v.Id && a.VoidedAtUtc == null
                orderby a.CreatedAtUtc
                select new
                {
                    a.Quantity,
                    product.Unit,
                    LotNumber = b == null ? null : b.LotNumber,
                    ExpiryDate = b == null ? (DateOnly?)null : b.ExpiryDate,
                })
            .FirstOrDefaultAsync(ct);

        var (animal, owner) = await AnimalAndOwnerAsync(tenantId, v.PetId, ct);
        var veterinarianId = await ResponsibleVeterinarianIdAsync(tenantId, v.ExaminationId, ct);
        var names = await NamesAsync([veterinarianId, v.HbsMarkedByUserId], ct);

        return new HbsVaccinationSource(
            v.Id,
            v.ClinicId,
            v.VaccineName,
            v.AppliedAtUtc,
            HbsTracking.IsTracked(v),
            administration?.LotNumber,
            administration?.ExpiryDate,
            administration?.Quantity,
            administration?.Unit,
            animal,
            owner,
            Name(names, veterinarianId),
            new HbsEntrySource(v.HbsStatus, v.HbsReferenceNumber, v.HbsNotRequiredReason, v.HbsMarkedAtUtc, v.HbsMarkedByUserId,
                Name(names, v.HbsMarkedByUserId)));
    }

    private async Task<(HbsAnimalSource Animal, HbsOwnerSource Owner)> AnimalAndOwnerAsync(Guid tenantId, Guid petId, CancellationToken ct)
    {
        var row = await (
                from pet in _db.Pets.AsNoTracking()
                join c in _db.Clients.AsNoTracking() on pet.ClientId equals c.Id
                join s in _db.Species.AsNoTracking() on pet.SpeciesId equals s.Id
                join b in _db.Breeds.AsNoTracking() on pet.BreedId equals b.Id into breeds
                from b in breeds.DefaultIfEmpty()
                where pet.TenantId == tenantId && pet.Id == petId
                select new
                {
                    PetName = pet.Name,
                    Species = s.Name,
                    Breed = b == null ? pet.Breed : b.Name,
                    pet.MicrochipNumber,
                    pet.HbsAnimalId,
                    c.FullName,
                    c.Phone,
                    c.PersonType,
                    c.NationalId,
                    c.TaxNumber,
                })
            .FirstOrDefaultAsync(ct);

        return row is null
            ? (new HbsAnimalSource(string.Empty, string.Empty, null, null, null), new HbsOwnerSource(string.Empty, null, null, null, null))
            : (new HbsAnimalSource(row.PetName, row.Species, row.Breed, row.MicrochipNumber, row.HbsAnimalId),
                new HbsOwnerSource(row.FullName, row.Phone, row.PersonType, row.NationalId, row.TaxNumber));
    }

    private Task<Guid?> ResponsibleVeterinarianIdAsync(Guid tenantId, Guid? examinationId, CancellationToken ct)
        => examinationId is not { } id
            ? Task.FromResult<Guid?>(null)
            : (from e in _db.Examinations.AsNoTracking()
               join visit in _db.Visits.AsNoTracking() on e.VisitId equals visit.Id
               where e.TenantId == tenantId && e.Id == id
               select visit.ResponsibleVeterinarianUserId)
                .FirstOrDefaultAsync(ct);

    private async Task<IReadOnlyDictionary<Guid, string?>> NamesAsync(Guid?[] userIds, CancellationToken ct)
    {
        var ids = userIds.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, string?>();

        var rows = await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToListAsync(ct);

        return rows.ToDictionary(u => u.Id, u => UserDisplayName.Resolve(u.DisplayName, u.Email));
    }

    private static string? Name(IReadOnlyDictionary<Guid, string?> names, Guid? userId)
        => userId is { } id && names.TryGetValue(id, out var name) ? name : null;
}
