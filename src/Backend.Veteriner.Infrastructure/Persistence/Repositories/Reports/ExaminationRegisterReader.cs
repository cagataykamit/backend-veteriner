using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Reports.Registers.Examinations;
using Backend.Veteriner.Domain.Examinations;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Reports;

/// <summary>
/// Muayene defteri: kapsamdaki muayeneler tarih sıralı sayfalanır; tedavi, reçete (satırlarıyla), uygulama (ürün adıyla)
/// ve hekim adları sayfa kümesiyle birer toplu sorguda okunur (N+1 yok). Hekim kuralı ve sütunlar sözleşme bölüm 4'tedir.
/// </summary>
public sealed class ExaminationRegisterReader : IRegisterReader<ExaminationRegisterItemDto>
{
    private readonly AppDbContext _db;

    public ExaminationRegisterReader(AppDbContext db)
        => _db = db;

    public async Task<RegisterReadResult<ExaminationRegisterItemDto>> ReadAsync(
        RegisterReadRequest request,
        CancellationToken ct = default)
    {
        var examinations = _db.Examinations.AsNoTracking()
            .Where(e => e.TenantId == request.TenantId
                        && e.ExaminedAtUtc >= request.FromUtc
                        && e.ExaminedAtUtc < request.ToUtcExclusive);

        if (request.ClinicId.HasValue)
        {
            var clinicId = request.ClinicId.Value;
            examinations = examinations.Where(e => e.ClinicId == clinicId);
        }
        else if (request.AccessibleClinicIds is { } accessible)
        {
            examinations = accessible.Count == 0
                ? examinations.Where(_ => false)
                : examinations.Where(e => accessible.Contains(e.ClinicId));
        }

        var totalCount = await examinations.CountAsync(ct);
        if (totalCount == 0 || request.Skip >= totalCount)
            return new RegisterReadResult<ExaminationRegisterItemDto>(totalCount, []);

        var page = examinations
            .OrderBy(e => e.ExaminedAtUtc).ThenBy(e => e.CreatedAtUtc).ThenBy(e => e.Id)
            .Skip(request.Skip).Take(request.Take);
        var pageIds = page.Select(e => e.Id);

        var rows = await (
            from e in page
            join p in _db.Pets.AsNoTracking() on e.PetId equals p.Id
            join c in _db.Clients.AsNoTracking() on p.ClientId equals c.Id
            join s in _db.Species.AsNoTracking() on p.SpeciesId equals s.Id
            join b in _db.Breeds.AsNoTracking() on p.BreedId equals b.Id into breeds
            from b in breeds.DefaultIfEmpty()
            join v in _db.Visits.AsNoTracking() on e.VisitId equals v.Id into visits
            from v in visits.DefaultIfEmpty()
            orderby e.ExaminedAtUtc, e.CreatedAtUtc, e.Id
            select new
            {
                e.Id,
                e.ExaminedAtUtc,
                e.CreatedAtUtc,
                e.Status,
                OwnerName = c.FullName,
                OwnerPhone = c.Phone,
                OwnerAddress = c.Address,
                PetName = p.Name,
                Species = s.Name,
                BreedName = b == null ? p.Breed : b.Name,
                p.Gender,
                p.BirthDate,
                p.MicrochipNumber,
                p.SpecialProtocolNumber,
                Complaint = e.VisitReason,
                Diagnosis = e.Assessment,
                e.Plan,
                VeterinarianUserId = v == null ? null : v.ResponsibleVeterinarianUserId,
            }).ToListAsync(ct);

        var treatments = (await _db.Treatments.AsNoTracking()
                .Where(t => t.TenantId == request.TenantId && pageIds.Contains(t.ExaminationId!.Value))
                .OrderBy(t => t.TreatmentDateUtc).ThenBy(t => t.CreatedAtUtc).ThenBy(t => t.Id)
                .Select(t => new { ExaminationId = t.ExaminationId!.Value, t.Title })
                .ToListAsync(ct))
            .ToLookup(t => t.ExaminationId, t => t.Title);

        var prescriptions = (await _db.Prescriptions.AsNoTracking()
                .Where(p => p.TenantId == request.TenantId && pageIds.Contains(p.ExaminationId!.Value))
                .OrderBy(p => p.PrescribedAtUtc).ThenBy(p => p.CreatedAtUtc).ThenBy(p => p.Id)
                .Select(p => new
                {
                    ExaminationId = p.ExaminationId!.Value,
                    p.Title,
                    Items = p.Items.OrderBy(i => i.SortOrder)
                        .Select(i => new RegisterPrescriptionLineDto(i.Name, i.Quantity, i.Instructions))
                        .ToList(),
                })
                .ToListAsync(ct))
            .ToLookup(p => p.ExaminationId, p => new RegisterPrescriptionDto(p.Title, p.Items));

        var administrations = (await (
                from a in _db.Administrations.AsNoTracking()
                join product in _db.Products.AsNoTracking() on a.ProductId equals product.Id
                where a.TenantId == request.TenantId
                      && pageIds.Contains(a.ExaminationId!.Value)
                      && (request.IncludeVoided || a.VoidedAtUtc == null)
                orderby a.AdministeredAtUtc, a.CreatedAtUtc, a.Id
                select new
                {
                    ExaminationId = a.ExaminationId!.Value,
                    Dto = new RegisterAdministrationDto(
                        product.Name, a.Quantity, product.Unit, a.AddedAfterFinalization, a.VoidedAtUtc != null),
                }).ToListAsync(ct))
            .ToLookup(a => a.ExaminationId, a => a.Dto);

        var veterinarianIds = rows.Where(r => r.VeterinarianUserId.HasValue).Select(r => r.VeterinarianUserId!.Value).Distinct().ToList();
        var veterinarianNames = veterinarianIds.Count == 0
            ? []
            : await _db.Users.AsNoTracking()
                .Where(u => veterinarianIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName ?? u.Email, ct);

        var items = rows.Select((r, i) => new ExaminationRegisterItemDto(
            request.Skip + i + 1,
            r.Id,
            r.ExaminedAtUtc,
            r.Status == ExaminationStatus.Finalized,
            r.OwnerName,
            r.OwnerPhone,
            r.OwnerAddress,
            r.PetName,
            r.Species,
            r.BreedName,
            RegisterDisplay.Gender(r.Gender),
            RegisterDisplay.Age(r.BirthDate, OperationDayBounds.ToLocalDate(r.ExaminedAtUtc)),
            CardNumber: null,
            r.MicrochipNumber,
            r.SpecialProtocolNumber,
            EPrescriptionId: null,
            r.Complaint,
            r.Diagnosis,
            TreatmentCell(treatments[r.Id], r.Plan),
            prescriptions[r.Id].ToList(),
            administrations[r.Id].ToList(),
            r.VeterinarianUserId is { } vetId && veterinarianNames.TryGetValue(vetId, out var vetName) ? vetName : null)).ToList();

        return new RegisterReadResult<ExaminationRegisterItemDto>(totalCount, items);
    }

    /// <summary>Tedavi şekli: bağlı tedavilerin başlığı; tedavi yoksa muayenenin plan metni.</summary>
    private static IReadOnlyList<string> TreatmentCell(IEnumerable<string> treatmentTitles, string? plan)
    {
        var titles = treatmentTitles.ToList();
        if (titles.Count == 0 && !string.IsNullOrWhiteSpace(plan))
            titles.Add(plan);
        return titles;
    }
}
