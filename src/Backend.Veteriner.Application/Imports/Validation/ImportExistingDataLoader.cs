using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ClientCredits;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Imports.Validation;

/// <summary>
/// Doğrulama için kiracının mevcut kayıtlarını <b>sabit sayıda</b> sorguyla okur: müşteriler (telefon, e-posta, ad kümeleri
/// ayrı ayrı), onların ve çiplerin hayvanları (ayrı ayrı), bulunan hayvanların uygulanmış aşıları; tam geçiş tabloları
/// (ADR-048) partide varsa ayrıca birer sorgu: içe aktarılmış klinik kayıtlar / hesap hareketleri, gelecek randevular,
/// ürünler, ürün kategorileri, kliniğin randevu nedenleri ve hekimleri. Ayrı sorguların sonuçları kimliğe göre
/// tekilleştirilir; sonuç kümesi birleşik koşulla aynıdır.
/// </summary>
public sealed class ImportExistingDataLoader
{
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly IReadRepository<ImportedClinicalRecord> _clinicalRecords;
    private readonly IReadRepository<ImportedAccountEntry> _accountEntries;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<AppointmentReason> _appointmentReasons;
    private readonly IReadRepository<Product> _products;
    private readonly IReadRepository<ProductCategory> _productCategories;
    private readonly IClinicVeterinarianReader _veterinarians;
    private readonly IReadRepository<ImportSourceLink> _links;
    private readonly IReadRepository<Charge> _charges;
    private readonly IReadRepository<Payment> _payments;
    private readonly IReadRepository<ClientCredit> _credits;

    public ImportExistingDataLoader(
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Vaccination> vaccinations,
        IReadRepository<ImportedClinicalRecord> clinicalRecords,
        IReadRepository<ImportedAccountEntry> accountEntries,
        IReadRepository<Appointment> appointments,
        IReadRepository<AppointmentReason> appointmentReasons,
        IReadRepository<Product> products,
        IReadRepository<ProductCategory> productCategories,
        IClinicVeterinarianReader veterinarians,
        IReadRepository<ImportSourceLink> links,
        IReadRepository<Charge> charges,
        IReadRepository<Payment> payments,
        IReadRepository<ClientCredit> credits)
    {
        _links = links;
        _charges = charges;
        _payments = payments;
        _credits = credits;
        _clients = clients;
        _pets = pets;
        _vaccinations = vaccinations;
        _clinicalRecords = clinicalRecords;
        _accountEntries = accountEntries;
        _appointments = appointments;
        _appointmentReasons = appointmentReasons;
        _products = products;
        _productCategories = productCategories;
        _veterinarians = veterinarians;
    }

    /// <param name="clinicId">Partinin kliniği (randevu nedeni, hekim dizini, kaynak kimliği bağı ve bakiye için).</param>
    /// <param name="profile">Partinin kaynak programı (kaynak kimliği bağı yalnız program profillerinde; ADR-051).</param>
    /// <param name="tables">Partide satırı olan tablolar; tam geçiş verisi yalnız ilgili tablo varsa okunur.</param>
    public async Task<ImportExistingData> LoadAsync(
        Guid tenantId,
        Guid? clinicId,
        ImportSourceProfile profile,
        ImportLookupKeys keys,
        IReadOnlyCollection<ImportTable> tables,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var clients = new Dictionary<Guid, ImportExistingClient>();

        // Kaynak kimliği bağları önce: hedefleri ad/telefon kümelerinde olmasa da (adı, telefonu değişmiş) okunur.
        var clientLinkTargets = await LinksAsync(tenantId, clinicId, profile, ImportSourceEntity.Client, keys.CustomerReferences, ct);
        if (clientLinkTargets.Count > 0)
        {
            AddById(clients, await _clients.ListAsync(
                new ImportExistingClientsByIdsSpec(tenantId, clientLinkTargets.Values.Distinct().ToArray()), ct), c => c.Id);
        }

        if (keys.Phones.Count > 0)
            AddById(clients, await _clients.ListAsync(ImportExistingClientsSpec.ByPhones(tenantId, keys.Phones), ct), c => c.Id);
        if (keys.Emails.Count > 0)
            AddById(clients, await _clients.ListAsync(ImportExistingClientsSpec.ByEmails(tenantId, keys.Emails), ct), c => c.Id);
        if (keys.Names.Count > 0)
            AddById(clients, await _clients.ListAsync(ImportExistingClientsSpec.ByNames(tenantId, keys.Names), ct), c => c.Id);

        var pets = new Dictionary<Guid, ImportExistingPet>();
        var petLinkTargets = await LinksAsync(tenantId, clinicId, profile, ImportSourceEntity.Pet, keys.PetReferences, ct);
        if (petLinkTargets.Count > 0)
        {
            AddById(pets, await _pets.ListAsync(
                new ImportExistingPetsByIdsSpec(tenantId, petLinkTargets.Values.Distinct().ToArray()), ct), p => p.Id);
        }

        if (clients.Count > 0)
            AddById(pets, await _pets.ListAsync(ImportExistingPetsSpec.ByClients(tenantId, clients.Keys), ct), p => p.Id);
        if (keys.Microchips.Count > 0)
            AddById(pets, await _pets.ListAsync(ImportExistingPetsSpec.ByMicrochips(tenantId, keys.Microchips), ct), p => p.Id);

        IReadOnlyList<ImportExistingVaccination> vaccinations = [];
        if (pets.Count > 0 && tables.Contains(ImportTable.Vaccinations))
            vaccinations = await _vaccinations.ListAsync(new ImportExistingVaccinationsSpec(tenantId, pets.Keys), ct);

        IReadOnlyList<ImportExistingClinicalRecord> clinicalRecords = [];
        if (pets.Count > 0 && tables.Contains(ImportTable.ClinicalRecords))
            clinicalRecords = await _clinicalRecords.ListAsync(new ImportExistingClinicalRecordsSpec(tenantId, pets.Keys), ct);

        IReadOnlyList<ImportExistingAccountEntry> accountEntries = [];
        if (clients.Count > 0 && tables.Contains(ImportTable.AccountEntries))
            accountEntries = await _accountEntries.ListAsync(new ImportExistingAccountEntriesSpec(tenantId, clients.Keys), ct);

        IReadOnlyList<ImportExistingAppointment> appointments = [];
        IReadOnlyList<ImportCatalogEntry> reasons = [];
        IReadOnlyList<ImportCatalogEntry> veterinarians = [];
        if (tables.Contains(ImportTable.Appointments) && clinicId is { } clinic)
        {
            if (pets.Count > 0)
                appointments = await _appointments.ListAsync(new ImportExistingAppointmentsSpec(tenantId, pets.Keys, nowUtc), ct);
            reasons = await _appointmentReasons.ListAsync(new ImportAppointmentReasonsSpec(tenantId, clinic), ct);
            veterinarians = (await _veterinarians.ListAsync(tenantId, clinic, ct))
                .Where(v => !string.IsNullOrWhiteSpace(v.Name))
                .Select(v => new ImportCatalogEntry(v.UserId, v.Name!, null, null))
                .ToList();
        }

        IReadOnlyList<ImportExistingProduct> products = [];
        IReadOnlyList<ImportCatalogEntry> categories = [];
        if (tables.Contains(ImportTable.Products))
        {
            products = await _products.ListAsync(new ImportExistingProductsSpec(tenantId), ct);
            categories = await _productCategories.ListAsync(new ImportProductCategoriesSpec(tenantId), ct);
        }

        return new ImportExistingData(
            clients.Values.OrderBy(c => c.Id).ToList(),
            pets.Values.OrderBy(p => p.Id).ToList(),
            vaccinations)
        {
            ClinicalRecords = clinicalRecords,
            AccountEntries = accountEntries,
            Appointments = appointments,
            Products = products,
            ProductCategories = categories,
            AppointmentReasons = reasons,
            Veterinarians = veterinarians,
            ClientLinks = clientLinkTargets.Where(kv => clients.ContainsKey(kv.Value)).ToDictionary(StringComparer.Ordinal),
            PetLinks = petLinkTargets.Where(kv => pets.ContainsKey(kv.Value)).ToDictionary(StringComparer.Ordinal),
            ClientBalances = keys.BalanceMapped && clinicId is { } balanceClinic
                ? await BalancesAsync(tenantId, balanceClinic, clients.Keys, ct)
                : null,
        };
    }

    /// <summary>Kalıcı kaynak anahtarlarının bağları (anahtar → hedef); program profili ve klinik yoksa boş.</summary>
    private async Task<Dictionary<string, Guid>> LinksAsync(
        Guid tenantId, Guid? clinicId, ImportSourceProfile profile, ImportSourceEntity entity, IReadOnlyList<string> references, CancellationToken ct)
    {
        var keys = references.Select(r => ImportSourceLink.StableKey(profile, r)).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        if (clinicId is not { } clinic || keys.Length == 0)
            return new Dictionary<string, Guid>(StringComparer.Ordinal);

        var links = await _links.ListAsync(new ImportSourceLinksSpec(tenantId, clinic, profile, entity, keys), ct);
        return links.ToDictionary(l => l.SourceKey, l => l.TargetId, StringComparer.Ordinal);
    }

    /// <summary>Net bakiye: kalan ücret − bağlanmamış ödeme − açık alacak (partinin kliniğinde; üç sorgu).</summary>
    private async Task<IReadOnlyDictionary<Guid, decimal>> BalancesAsync(
        Guid tenantId, Guid clinicId, IReadOnlyCollection<Guid> clientIds, CancellationToken ct)
    {
        if (clientIds.Count == 0)
            return new Dictionary<Guid, decimal>();

        var parts = new List<ImportBalancePart>();
        parts.AddRange(await _charges.ListAsync(new ImportClientOutstandingChargesSpec(tenantId, clinicId, clientIds), ct));
        parts.AddRange(await _payments.ListAsync(new ImportClientUnallocatedPaymentsSpec(tenantId, clinicId, clientIds), ct));
        parts.AddRange(await _credits.ListAsync(new ImportClientOpenCreditsSpec(tenantId, clinicId, clientIds), ct));
        return parts.GroupBy(p => p.ClientId).ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
    }

    private static void AddById<T>(Dictionary<Guid, T> target, IEnumerable<T> items, Func<T, Guid> id)
    {
        foreach (var item in items)
            target.TryAdd(id(item), item);
    }
}
