using System.Globalization;
using Backend.Veteriner.Application.Hbs;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Reports.Registers.Hbs;
using Backend.Veteriner.Application.Users.Common;
using Backend.Veteriner.Domain.Hbs;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Reports;

/// <summary>
/// HBS takibi — reçeteler: tarih aralığındaki, istenen HBS durumundaki (varsayılan Girilmedi) reçeteler; hekim süzgeci ve hekim adı
/// defter kuralıyla (geliş sorumlu hekimi). Sözleşme: HBS_ASSISTANT_API_CONTRACT.md bölüm 6.
/// </summary>
public sealed class HbsPrescriptionRegisterReader : IRegisterReader<HbsPrescriptionRegisterItemDto>
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private readonly AppDbContext _db;

    public HbsPrescriptionRegisterReader(AppDbContext db) => _db = db;

    public async Task<RegisterReadResult<HbsPrescriptionRegisterItemDto>> ReadAsync(RegisterReadRequest request, CancellationToken ct = default)
    {
        var prescriptions = _db.Prescriptions.AsNoTracking()
            .Where(p => p.TenantId == request.TenantId
                        && p.PrescribedAtUtc >= request.FromUtc
                        && p.PrescribedAtUtc < request.ToUtcExclusive)
            .Where(HbsTracking.PrescriptionWithStatus(request.HbsStatus ?? HbsEntryStatus.NotEntered));

        if (request.ClinicId.HasValue)
        {
            var clinicId = request.ClinicId.Value;
            prescriptions = prescriptions.Where(p => p.ClinicId == clinicId);
        }
        else if (request.AccessibleClinicIds is { } accessible)
        {
            prescriptions = accessible.Count == 0
                ? prescriptions.Where(_ => false)
                : prescriptions.Where(p => accessible.Contains(p.ClinicId));
        }

        if (request.VeterinarianUserId is { } veterinarianId)
        {
            prescriptions = prescriptions.Where(p => _db.Examinations.Any(e => e.Id == p.ExaminationId
                && _db.Visits.Any(v => v.Id == e.VisitId && v.ResponsibleVeterinarianUserId == veterinarianId)));
        }

        var totalCount = await prescriptions.CountAsync(ct);
        if (totalCount == 0 || request.Skip >= totalCount)
            return new RegisterReadResult<HbsPrescriptionRegisterItemDto>(totalCount, []);

        var page = prescriptions
            .OrderBy(p => p.PrescribedAtUtc).ThenBy(p => p.CreatedAtUtc).ThenBy(p => p.Id)
            .Skip(request.Skip).Take(request.Take);

        var rows = await (
            from p in page
            join pet in _db.Pets.AsNoTracking() on p.PetId equals pet.Id
            join c in _db.Clients.AsNoTracking() on pet.ClientId equals c.Id
            join s in _db.Species.AsNoTracking() on pet.SpeciesId equals s.Id
            join e in _db.Examinations.AsNoTracking() on p.ExaminationId equals e.Id into examinations
            from e in examinations.DefaultIfEmpty()
            join v in _db.Visits.AsNoTracking() on (e == null ? null : e.VisitId) equals v.Id into visits
            from v in visits.DefaultIfEmpty()
            orderby p.PrescribedAtUtc, p.CreatedAtUtc, p.Id
            select new
            {
                p.Id,
                p.PrescribedAtUtc,
                p.Title,
                p.Content,
                OwnerName = c.FullName,
                OwnerPhone = c.Phone,
                PetName = pet.Name,
                Species = s.Name,
                pet.MicrochipNumber,
                pet.HbsAnimalId,
                Items = p.Items.OrderBy(i => i.SortOrder).Select(i => new { i.Name, i.Quantity, i.Unit }).ToList(),
                VeterinarianUserId = v == null ? null : v.ResponsibleVeterinarianUserId,
                p.HbsStatus,
                p.HbsReferenceNumber,
                p.HbsMarkedAtUtc,
            }).ToListAsync(ct);

        var names = await HbsRegisterNames.ResolveAsync(_db, rows.Select(r => r.VeterinarianUserId), ct);

        var items = rows.Select((r, i) => new HbsPrescriptionRegisterItemDto(
            request.Skip + i + 1,
            r.Id,
            r.PrescribedAtUtc,
            r.OwnerName,
            r.OwnerPhone,
            r.PetName,
            r.Species,
            r.MicrochipNumber,
            r.HbsAnimalId,
            r.Title,
            r.Items.Count == 0
                ? r.Content
                : string.Join("; ", r.Items.Select(item => MedicationText(item.Name, item.Quantity, item.Unit))),
            HbsRegisterNames.Name(names, r.VeterinarianUserId),
            r.HbsStatus,
            r.HbsReferenceNumber,
            r.HbsMarkedAtUtc)).ToList();

        return new RegisterReadResult<HbsPrescriptionRegisterItemDto>(totalCount, items);
    }

    /// <summary><c>Ad ×Miktar Birim</c>; boş parçalar atlanır.</summary>
    private static string MedicationText(string name, decimal? quantity, string? unit)
        => quantity is { } q ? $"{name} ×{q.ToString("0.###", Turkish)} {unit}".TrimEnd() : name;
}

/// <summary>
/// HBS takibi — aşılar: takip kapsamındaki (uygulanmış, içe aktarılmamış) ve istenen HBS durumundaki aşılar; seri no/SKT bağlı
/// iptal edilmemiş uygulamanın partisinden. Sözleşme: HBS_ASSISTANT_API_CONTRACT.md bölüm 6.
/// </summary>
public sealed class HbsVaccinationRegisterReader : IRegisterReader<HbsVaccinationRegisterItemDto>
{
    private readonly AppDbContext _db;

    public HbsVaccinationRegisterReader(AppDbContext db) => _db = db;

    public async Task<RegisterReadResult<HbsVaccinationRegisterItemDto>> ReadAsync(RegisterReadRequest request, CancellationToken ct = default)
    {
        var vaccinations = _db.Vaccinations.AsNoTracking()
            .Where(v => v.TenantId == request.TenantId
                        && v.AppliedAtUtc >= request.FromUtc
                        && v.AppliedAtUtc < request.ToUtcExclusive)
            .Where(HbsTracking.VaccinationTrackedWithStatus(request.HbsStatus ?? HbsEntryStatus.NotEntered));

        if (request.ClinicId.HasValue)
        {
            var clinicId = request.ClinicId.Value;
            vaccinations = vaccinations.Where(v => v.ClinicId == clinicId);
        }
        else if (request.AccessibleClinicIds is { } accessible)
        {
            vaccinations = accessible.Count == 0
                ? vaccinations.Where(_ => false)
                : vaccinations.Where(v => accessible.Contains(v.ClinicId));
        }

        if (request.VeterinarianUserId is { } veterinarianId)
        {
            vaccinations = vaccinations.Where(x => _db.Examinations.Any(e => e.Id == x.ExaminationId
                && _db.Visits.Any(v => v.Id == e.VisitId && v.ResponsibleVeterinarianUserId == veterinarianId)));
        }

        var totalCount = await vaccinations.CountAsync(ct);
        if (totalCount == 0 || request.Skip >= totalCount)
            return new RegisterReadResult<HbsVaccinationRegisterItemDto>(totalCount, []);

        var page = vaccinations
            .OrderBy(v => v.AppliedAtUtc).ThenBy(v => v.CreatedAtUtc).ThenBy(v => v.Id)
            .Skip(request.Skip).Take(request.Take);

        var rows = await (
            from x in page
            join pet in _db.Pets.AsNoTracking() on x.PetId equals pet.Id
            join c in _db.Clients.AsNoTracking() on pet.ClientId equals c.Id
            join s in _db.Species.AsNoTracking() on pet.SpeciesId equals s.Id
            join e in _db.Examinations.AsNoTracking() on x.ExaminationId equals e.Id into examinations
            from e in examinations.DefaultIfEmpty()
            join v in _db.Visits.AsNoTracking() on (e == null ? null : e.VisitId) equals v.Id into visits
            from v in visits.DefaultIfEmpty()
            orderby x.AppliedAtUtc, x.CreatedAtUtc, x.Id
            select new
            {
                x.Id,
                AppliedAtUtc = x.AppliedAtUtc!.Value,
                OwnerName = c.FullName,
                OwnerPhone = c.Phone,
                PetName = pet.Name,
                Species = s.Name,
                pet.MicrochipNumber,
                pet.HbsAnimalId,
                x.VaccineName,
                Batch = (from a in _db.Administrations
                         join b in _db.StockBatches on a.BatchId equals b.Id
                         where a.VaccinationId == x.Id && a.VoidedAtUtc == null
                         orderby a.CreatedAtUtc
                         select new { b.LotNumber, b.ExpiryDate }).FirstOrDefault(),
                VeterinarianUserId = v == null ? null : v.ResponsibleVeterinarianUserId,
                x.HbsStatus,
                x.HbsReferenceNumber,
                x.HbsMarkedAtUtc,
            }).ToListAsync(ct);

        var names = await HbsRegisterNames.ResolveAsync(_db, rows.Select(r => r.VeterinarianUserId), ct);

        var items = rows.Select((r, i) => new HbsVaccinationRegisterItemDto(
            request.Skip + i + 1,
            r.Id,
            r.AppliedAtUtc,
            r.OwnerName,
            r.OwnerPhone,
            r.PetName,
            r.Species,
            r.MicrochipNumber,
            r.HbsAnimalId,
            r.VaccineName,
            r.Batch?.LotNumber,
            r.Batch?.ExpiryDate,
            HbsRegisterNames.Name(names, r.VeterinarianUserId),
            r.HbsStatus,
            r.HbsReferenceNumber,
            r.HbsMarkedAtUtc)).ToList();

        return new RegisterReadResult<HbsVaccinationRegisterItemDto>(totalCount, items);
    }
}

/// <summary>Sayfadaki hekim kimliklerinin görünen adları tek toplu sorguda (N+1 yok).</summary>
internal static class HbsRegisterNames
{
    public static async Task<IReadOnlyDictionary<Guid, string?>> ResolveAsync(AppDbContext db, IEnumerable<Guid?> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, string?>();

        var rows = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToListAsync(ct);

        return rows.ToDictionary(u => u.Id, u => UserDisplayName.Resolve(u.DisplayName, u.Email));
    }

    public static string? Name(IReadOnlyDictionary<Guid, string?> names, Guid? userId)
        => userId is { } id && names.TryGetValue(id, out var name) ? name : null;
}
