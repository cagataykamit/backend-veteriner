using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Rollback;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Imports;

/// <summary>
/// Engelli kayıtları tanıtan bilgileri okur (kayıt türü başına bir sorgu, ardından hastalar, sahipler ve türler).
/// Kişisel veri taşır; burada günlük yazılmaz. Kiracı süzgeci açıkça uygulanır.
/// </summary>
public sealed class ImportRollbackRecordReader : IImportRollbackRecordReader
{
    private readonly AppDbContext _db;

    public ImportRollbackRecordReader(AppDbContext db) => _db = db;

    public async Task<IReadOnlyDictionary<Guid, ImportRollbackRecordDto>> ReadAsync(
        Guid tenantId, IReadOnlyCollection<ImportRollbackItem> items, CancellationToken ct)
    {
        Guid[] Ids(ImportTable table) => items.Where(i => i.Row.Table == table).Select(i => i.TargetId).Distinct().ToArray();
        var clientIds = Ids(ImportTable.Customers);
        var petIds = Ids(ImportTable.Pets);
        var appointmentIds = Ids(ImportTable.Appointments);
        var vaccinationIds = Ids(ImportTable.Vaccinations);
        var clinicalRecordIds = Ids(ImportTable.ClinicalRecords);
        var accountEntryIds = Ids(ImportTable.AccountEntries);
        var productIds = Ids(ImportTable.Products);

        var appointments = appointmentIds.Length == 0 ? [] : await _db.Appointments.AsNoTracking()
            .Where(a => a.TenantId == tenantId && appointmentIds.Contains(a.Id))
            .Select(a => new { a.Id, a.PetId, a.ScheduledAtUtc, a.AppointmentType })
            .ToListAsync(ct);
        var vaccinations = vaccinationIds.Length == 0 ? [] : await _db.Vaccinations.AsNoTracking()
            .Where(v => v.TenantId == tenantId && vaccinationIds.Contains(v.Id))
            .Select(v => new { v.Id, v.PetId, v.VaccineName, At = v.AppliedAtUtc ?? v.DueAtUtc })
            .ToListAsync(ct);
        var clinicalRecords = clinicalRecordIds.Length == 0 ? [] : await _db.ImportedClinicalRecords.AsNoTracking()
            .Where(r => r.TenantId == tenantId && clinicalRecordIds.Contains(r.Id))
            .Select(r => new { r.Id, r.PetId, r.RecordedOn, r.RecordType })
            .ToListAsync(ct);
        var accountEntries = accountEntryIds.Length == 0 ? [] : await _db.ImportedAccountEntries.AsNoTracking()
            .Where(e => e.TenantId == tenantId && accountEntryIds.Contains(e.Id))
            .Select(e => new { e.Id, e.ClientId, e.OccurredOn, e.Kind, e.Description })
            .ToListAsync(ct);
        var products = productIds.Length == 0 ? [] : await _db.Products.AsNoTracking()
            .Where(p => p.TenantId == tenantId && productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(ct);

        var petLookupIds = petIds
            .Concat(appointments.Select(a => a.PetId))
            .Concat(vaccinations.Select(v => v.PetId))
            .Concat(clinicalRecords.Select(r => r.PetId))
            .Distinct()
            .ToArray();
        var pets = petLookupIds.Length == 0 ? [] : await _db.Pets.AsNoTracking()
            .Where(p => p.TenantId == tenantId && petLookupIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.ClientId, p.SpeciesId })
            .ToListAsync(ct);
        var petById = pets.ToDictionary(p => p.Id);

        var speciesIds = pets.Select(p => p.SpeciesId).Distinct().ToArray();
        var species = speciesIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await _db.Species.AsNoTracking()
                .Where(s => speciesIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var clientLookupIds = clientIds
            .Concat(pets.Select(p => p.ClientId))
            .Concat(accountEntries.Select(e => e.ClientId))
            .Distinct()
            .ToArray();
        var clientNames = clientLookupIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await _db.Clients.AsNoTracking()
                .Where(c => c.TenantId == tenantId && clientLookupIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.FullName, ct);

        ImportRollbackRecordDto ForPet(Guid petId, string? name = null, DateTime? atUtc = null, DateOnly? on = null, string? kind = null)
        {
            if (!petById.TryGetValue(petId, out var pet))
                return new ImportRollbackRecordDto(Name: name, PetId: petId, AtUtc: atUtc, On: on, Kind: kind);

            return new ImportRollbackRecordDto(
                Name: name,
                Species: species.GetValueOrDefault(pet.SpeciesId),
                PetId: pet.Id,
                PetName: pet.Name,
                ClientId: pet.ClientId,
                OwnerName: clientNames.GetValueOrDefault(pet.ClientId),
                AtUtc: atUtc,
                On: on,
                Kind: kind);
        }

        var records = new Dictionary<Guid, ImportRollbackRecordDto>();
        foreach (var id in clientIds.Where(clientNames.ContainsKey))
            records[id] = new ImportRollbackRecordDto(Name: clientNames[id], ClientId: id, OwnerName: clientNames[id]);
        foreach (var id in petIds.Where(petById.ContainsKey))
            records[id] = ForPet(id, name: petById[id].Name);
        foreach (var a in appointments)
            records[a.Id] = ForPet(a.PetId, atUtc: a.ScheduledAtUtc, kind: a.AppointmentType.ToString());
        foreach (var v in vaccinations)
            records[v.Id] = ForPet(v.PetId, name: v.VaccineName, atUtc: v.At);
        foreach (var r in clinicalRecords)
            records[r.Id] = ForPet(r.PetId, name: r.RecordType, on: r.RecordedOn);
        foreach (var e in accountEntries)
        {
            records[e.Id] = new ImportRollbackRecordDto(
                Name: e.Description,
                ClientId: e.ClientId,
                OwnerName: clientNames.GetValueOrDefault(e.ClientId),
                On: e.OccurredOn,
                Kind: e.Kind.ToString());
        }

        foreach (var p in products)
            records[p.Id] = new ImportRollbackRecordDto(Name: p.Name);

        return records;
    }
}
