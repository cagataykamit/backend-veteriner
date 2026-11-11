using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
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
        IClinicVeterinarianReader veterinarians)
    {
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

    /// <param name="clinicId">Partinin kliniği (randevu nedeni ve hekim dizini için).</param>
    /// <param name="tables">Partide satırı olan tablolar; tam geçiş verisi yalnız ilgili tablo varsa okunur.</param>
    public async Task<ImportExistingData> LoadAsync(
        Guid tenantId,
        Guid? clinicId,
        ImportLookupKeys keys,
        IReadOnlyCollection<ImportTable> tables,
        DateTime nowUtc,
        CancellationToken ct)
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
        };
    }

    private static void AddById<T>(Dictionary<Guid, T> target, IEnumerable<T> items, Func<T, Guid> id)
    {
        foreach (var item in items)
            target.TryAdd(id(item), item);
    }
}
