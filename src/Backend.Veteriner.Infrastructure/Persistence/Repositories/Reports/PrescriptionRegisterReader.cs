using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Reports.Registers.Prescriptions;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Reports;

/// <summary>
/// Reçete defteri: kapsamdaki reçeteler tarih sıralı sayfalanır (sayfa ve sayım reçete bazlı); satırlar reçeteyle birlikte
/// tek sorguda gelir. Satırsız (eski, serbest metin) reçete tek ilaç satırı verir (ad = <c>Content</c>). Sütunlar ve hekim kuralı sözleşmede.
/// </summary>
public sealed class PrescriptionRegisterReader : IRegisterReader<PrescriptionRegisterEntryDto>
{
    private readonly AppDbContext _db;

    public PrescriptionRegisterReader(AppDbContext db)
        => _db = db;

    public async Task<RegisterReadResult<PrescriptionRegisterEntryDto>> ReadAsync(
        RegisterReadRequest request,
        CancellationToken ct = default)
    {
        var prescriptions = _db.Prescriptions.AsNoTracking()
            .Where(p => p.TenantId == request.TenantId
                        && p.PrescribedAtUtc >= request.FromUtc
                        && p.PrescribedAtUtc < request.ToUtcExclusive);

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

        var totalCount = await prescriptions.CountAsync(ct);
        if (totalCount == 0 || request.Skip >= totalCount)
            return new RegisterReadResult<PrescriptionRegisterEntryDto>(totalCount, []);

        var page = prescriptions
            .OrderBy(p => p.PrescribedAtUtc).ThenBy(p => p.CreatedAtUtc).ThenBy(p => p.Id)
            .Skip(request.Skip).Take(request.Take);

        var rows = await (
            from p in page
            join pet in _db.Pets.AsNoTracking() on p.PetId equals pet.Id
            join c in _db.Clients.AsNoTracking() on pet.ClientId equals c.Id
            join s in _db.Species.AsNoTracking() on pet.SpeciesId equals s.Id
            join clinic in _db.Clinics.AsNoTracking() on p.ClinicId equals clinic.Id
            join e in _db.Examinations.AsNoTracking() on p.ExaminationId equals e.Id into examinations
            from e in examinations.DefaultIfEmpty()
            join v in _db.Visits.AsNoTracking() on (e == null ? null : e.VisitId) equals v.Id into visits
            from v in visits.DefaultIfEmpty()
            orderby p.PrescribedAtUtc, p.CreatedAtUtc, p.Id
            select new
            {
                p.Id,
                p.PrescribedAtUtc,
                p.Content,
                ClinicAddress = clinic.Address,
                OwnerName = c.FullName,
                OwnerAddress = c.Address,
                Species = s.Name,
                Items = p.Items.OrderBy(i => i.SortOrder)
                    .Select(i => new { i.Name, i.Quantity })
                    .ToList(),
                VeterinarianUserId = (v == null ? null : v.ResponsibleVeterinarianUserId)
                                     ?? (e == null ? null : e.FinalizedByUserId),
            }).ToListAsync(ct);

        var veterinarianIds = rows.Where(r => r.VeterinarianUserId.HasValue).Select(r => r.VeterinarianUserId!.Value).Distinct().ToList();
        var veterinarianNames = veterinarianIds.Count == 0
            ? []
            : await _db.Users.AsNoTracking()
                .Where(u => veterinarianIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName ?? u.Email, ct);

        var entries = rows.Select((r, i) => new PrescriptionRegisterEntryDto(
            request.Skip + i + 1,
            r.Id,
            r.PrescribedAtUtc,
            PrescriptionClass: null,
            VolumeSequenceNumber: null,
            r.VeterinarianUserId is { } vetId && veterinarianNames.TryGetValue(vetId, out var vetName) ? vetName : null,
            r.ClinicAddress,
            r.OwnerName,
            r.OwnerAddress,
            r.Species,
            EarTagNumber: null,
            AnimalCount: 1,
            r.Items.Count == 0
                ? [new PrescriptionRegisterItemDto(r.Content, null, null, null)]
                : r.Items.Select(item => new PrescriptionRegisterItemDto(item.Name, null, null, item.Quantity)).ToList())).ToList();

        return new RegisterReadResult<PrescriptionRegisterEntryDto>(totalCount, entries);
    }
}
