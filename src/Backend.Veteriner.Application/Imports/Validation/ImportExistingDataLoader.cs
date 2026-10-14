using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Imports.Validation;

/// <summary>
/// Doğrulama için kiracının mevcut müşteri / hayvan / aşı kayıtlarını <b>sabit sayıda</b> sorguyla (en çok 3) okur:
/// müşteriler (telefon/e-posta/ad kümeleri), onların ve çiplerin hayvanları, bulunan hayvanların uygulanmış aşıları.
/// </summary>
public sealed class ImportExistingDataLoader
{
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Vaccination> _vaccinations;

    public ImportExistingDataLoader(
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Vaccination> vaccinations)
    {
        _clients = clients;
        _pets = pets;
        _vaccinations = vaccinations;
    }

    public async Task<ImportExistingData> LoadAsync(Guid tenantId, ImportLookupKeys keys, CancellationToken ct)
    {
        IReadOnlyList<ImportExistingClient> clients = [];
        if (keys.Phones.Count + keys.Emails.Count + keys.Names.Count > 0)
            clients = await _clients.ListAsync(new ImportExistingClientsSpec(tenantId, keys.Phones, keys.Emails, keys.Names), ct);

        IReadOnlyList<ImportExistingPet> pets = [];
        if (clients.Count + keys.Microchips.Count > 0)
            pets = await _pets.ListAsync(new ImportExistingPetsSpec(tenantId, clients.Select(c => c.Id).ToArray(), keys.Microchips), ct);

        IReadOnlyList<ImportExistingVaccination> vaccinations = [];
        if (pets.Count > 0)
            vaccinations = await _vaccinations.ListAsync(new ImportExistingVaccinationsSpec(tenantId, pets.Select(p => p.Id).ToArray()), ct);

        return new ImportExistingData(clients, pets, vaccinations);
    }
}
