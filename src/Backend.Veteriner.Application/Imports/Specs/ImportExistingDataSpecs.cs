using Ardalis.Specification;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Vaccinations;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Imports.Specs;

/// <summary>
/// Dosyadaki telefon / e-posta / ad kümelerinden herhangi biriyle eşleşen mevcut müşteriler (tek sorgu; kümeler
/// parametre olarak gider, satır başına sorgu yok; ad, ClientFullNameDuplicateKey ile aynı Türkçe/aksan duyarsız kuralla). Peşin müşteri hariç. Nihai eşleşme motorda katlanmış anahtarla yapılır.
/// </summary>
public sealed class ImportExistingClientsSpec : Specification<Client, ImportExistingClient>
{
    public ImportExistingClientsSpec(
        Guid tenantId,
        IReadOnlyCollection<string> phones,
        IReadOnlyCollection<string> emails,
        IReadOnlyCollection<string> names)
    {
        var phoneSet = phones.ToArray();
        var emailSet = emails.ToArray();
        var nameSet = names.Select(Client.NormalizeFullNameForDuplicateCheck).Distinct().ToArray();

        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId
                         && !c.IsWalkIn
                         && ((c.PhoneNormalized != null && phoneSet.Contains(c.PhoneNormalized))
                             || (c.Email != null && emailSet.Contains(c.Email))
                             || nameSet.Contains(EF.Functions.Collate(c.FullName.Trim().Replace("ı", "i").Replace("İ", "I").Replace("  ", " ").Replace("  ", " "), DailySearchTerm.ColumnCollation))));
        Query.OrderBy(c => c.Id);
        Query.Select(c => new ImportExistingClient(c.Id, c.FullName, c.PhoneNormalized, c.Email));
    }
}

/// <summary>Bulunan müşterilerin hayvanları ve dosyadaki mikroçiplerle eşleşen hayvanlar (tek sorgu).</summary>
public sealed class ImportExistingPetsSpec : Specification<Pet, ImportExistingPet>
{
    public ImportExistingPetsSpec(Guid tenantId, IReadOnlyCollection<Guid> clientIds, IReadOnlyCollection<string> microchips)
    {
        var clientSet = clientIds.ToArray();
        var chipSet = microchips.ToArray();

        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId
                         && (clientSet.Contains(p.ClientId)
                             || (p.MicrochipNumber != null && chipSet.Contains(p.MicrochipNumber))));
        Query.OrderBy(p => p.Id);
        Query.Select(p => new ImportExistingPet(p.Id, p.ClientId, p.Name, p.SpeciesId, p.MicrochipNumber, p.BirthDate));
    }
}

/// <summary>Bulunan hayvanların uygulanmış aşıları (mükerrer aşı denetimi için; tek sorgu).</summary>
public sealed class ImportExistingVaccinationsSpec : Specification<Vaccination, ImportExistingVaccination>
{
    public ImportExistingVaccinationsSpec(Guid tenantId, IReadOnlyCollection<Guid> petIds)
    {
        var petSet = petIds.ToArray();

        Query.AsNoTracking();
        Query.Where(v => v.TenantId == tenantId
                         && v.Status == VaccinationStatus.Applied
                         && v.VaccineDefinitionId != null
                         && petSet.Contains(v.PetId));
        Query.Select(v => new ImportExistingVaccination(v.PetId, v.VaccineDefinitionId!.Value, v.AppliedAtUtc));
    }
}
