using System.Linq.Expressions;
using Ardalis.Specification;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Vaccinations;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Imports.Specs;

/// <summary>
/// Dosyadaki telefon / e-posta / ad kümelerinden <b>biriyle</b> eşleşen mevcut müşteriler (küme başına bir sorgu; küme
/// parametre olarak gider, satır başına sorgu yok; ad, ClientFullNameDuplicateKey ile aynı Türkçe/aksan duyarsız kuralla).
/// Peşin müşteri hariç. Kümeler <c>OR</c> ile tek sorguda birleştirilmez: birleşik koşulda plan her kayıt için tüm
/// kümeleri tarıyordu (kayıt × anahtar); ayrı sorgularda her biri yarı birleştirme olur, sonuçlar bellekte birleşir.
/// Nihai eşleşme motorda katlanmış anahtarla yapılır.
/// </summary>
public sealed class ImportExistingClientsSpec : Specification<Client, ImportExistingClient>
{
    private ImportExistingClientsSpec(Guid tenantId, Expression<Func<Client, bool>> keyFilter)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && !c.IsWalkIn);
        Query.Where(keyFilter);
        Query.Select(c => new ImportExistingClient(c.Id, c.FullName, c.PhoneNormalized, c.Email));
    }

    public static ImportExistingClientsSpec ByPhones(Guid tenantId, IReadOnlyCollection<string> phones)
    {
        var phoneSet = phones.ToArray();
        return new(tenantId, c => c.PhoneNormalized != null && phoneSet.Contains(c.PhoneNormalized));
    }

    public static ImportExistingClientsSpec ByEmails(Guid tenantId, IReadOnlyCollection<string> emails)
    {
        var emailSet = emails.ToArray();
        return new(tenantId, c => c.Email != null && emailSet.Contains(c.Email));
    }

    public static ImportExistingClientsSpec ByNames(Guid tenantId, IReadOnlyCollection<string> names)
    {
        var nameSet = names.Select(Client.NormalizeFullNameForDuplicateCheck).Distinct().ToArray();
        return new(tenantId, c => nameSet.Contains(EF.Functions.Collate(
            c.FullName.Trim().Replace("ı", "i").Replace("İ", "I").Replace("  ", " ").Replace("  ", " "),
            DailySearchTerm.ColumnCollation)));
    }
}

/// <summary>
/// Bulunan müşterilerin hayvanları ya da dosyadaki mikroçiplerle eşleşen hayvanlar (anahtar türü başına bir sorgu;
/// <see cref="ImportExistingClientsSpec"/> ile aynı gerekçeyle <c>OR</c> ile birleştirilmez).
/// </summary>
public sealed class ImportExistingPetsSpec : Specification<Pet, ImportExistingPet>
{
    private ImportExistingPetsSpec(Guid tenantId, Expression<Func<Pet, bool>> keyFilter)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId);
        Query.Where(keyFilter);
        Query.Select(p => new ImportExistingPet(p.Id, p.ClientId, p.Name, p.SpeciesId, p.MicrochipNumber, p.BirthDate));
    }

    public static ImportExistingPetsSpec ByClients(Guid tenantId, IReadOnlyCollection<Guid> clientIds)
    {
        var clientSet = clientIds.ToArray();
        return new(tenantId, p => clientSet.Contains(p.ClientId));
    }

    public static ImportExistingPetsSpec ByMicrochips(Guid tenantId, IReadOnlyCollection<string> microchips)
    {
        var chipSet = microchips.ToArray();
        return new(tenantId, p => p.MicrochipNumber != null && chipSet.Contains(p.MicrochipNumber));
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
