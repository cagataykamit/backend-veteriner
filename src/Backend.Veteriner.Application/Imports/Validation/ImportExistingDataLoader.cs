using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Imports.Validation;

/// <summary>
/// Doğrulama için kiracının mevcut müşteri / hayvan / aşı kayıtlarını <b>sabit sayıda</b> sorguyla (en çok 6) okur:
/// müşteriler (telefon, e-posta, ad kümeleri ayrı ayrı), onların ve çiplerin hayvanları (ayrı ayrı), bulunan hayvanların
/// uygulanmış aşıları. Ayrı sorguların sonuçları kimliğe göre tekilleştirilir; sonuç kümesi birleşik koşulla aynıdır.
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
        var clients = new Dictionary<Guid, ImportExistingClient>();
        if (keys.Phones.Count > 0)
            AddById(clients, await _clients.ListAsync(ImportExistingClientsSpec.ByPhones(tenantId, keys.Phones), ct), c => c.Id);
        if (keys.Emails.Count > 0)
            AddById(clients, await _clients.ListAsync(ImportExistingClientsSpec.ByEmails(tenantId, keys.Emails), ct), c => c.Id);
        if (keys.Names.Count > 0)
            AddById(clients, await _clients.ListAsync(ImportExistingClientsSpec.ByNames(tenantId, keys.Names), ct), c => c.Id);

        var pets = new Dictionary<Guid, ImportExistingPet>();
        if (clients.Count > 0)
            AddById(pets, await _pets.ListAsync(ImportExistingPetsSpec.ByClients(tenantId, clients.Keys), ct), p => p.Id);
        if (keys.Microchips.Count > 0)
            AddById(pets, await _pets.ListAsync(ImportExistingPetsSpec.ByMicrochips(tenantId, keys.Microchips), ct), p => p.Id);

        IReadOnlyList<ImportExistingVaccination> vaccinations = [];
        if (pets.Count > 0)
            vaccinations = await _vaccinations.ListAsync(new ImportExistingVaccinationsSpec(tenantId, pets.Keys), ct);

        return new ImportExistingData(
            clients.Values.OrderBy(c => c.Id).ToList(),
            pets.Values.OrderBy(p => p.Id).ToList(),
            vaccinations);
    }

    private static void AddById<T>(Dictionary<Guid, T> target, IEnumerable<T> items, Func<T, Guid> id)
    {
        foreach (var item in items)
            target.TryAdd(id(item), item);
    }
}
