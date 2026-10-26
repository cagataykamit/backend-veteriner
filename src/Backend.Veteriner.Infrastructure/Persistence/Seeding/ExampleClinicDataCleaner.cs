using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.ExampleClinic;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.CareInstructions;
using Backend.Veteriner.Domain.ChargeSuggestions;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Imaging;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Reminders;
using Backend.Veteriner.Domain.Sales;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Domain.Visits;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

/// <summary>
/// Örnek Veteriner Kliniği verisini bulur ve kalıcı siler (EXAMPLE_CLINIC_API_CONTRACT.md §10). Örnek kayıt tanımı:
/// <list type="bullet">
///   <item>Müşteri: adres <see cref="ExampleClinicDataGenerator.TagPrefix"/> ile başlar.</item>
///   <item>Hasta: sahibi örnek müşteri ya da notu işaretli.</item>
///   <item>Örnek müşteri/hastaya bağlı her kayıt (kullanıcının sonradan eklediği dahil; işaretten bağımsız).</item>
///   <item>Ürün: açıklaması işaretli. Silinmeyecek bir kayıt (satış satırı, uygulama, ücret önerisi, şablon satırı, reçete
///   kalemi, işaretsiz stok hareketi) ona bağlıysa silinmez, aktifse pasife alınır; pasif ve kullanılmış ürün örnek sayılmaz.</item>
/// </list>
/// Silme küme tabanlıdır (<c>ExecuteDelete</c>) ve çağıranın işlemindedir; bağımlı kayıtlar önce silinir. Okuma modelleri
/// için müşteri/hasta silme, randevu silme ve ödeme iptal olayları silmeden önce aynı işlemde outbox'a yazılır. Ücret,
/// ödeme ve tahsislerdeki "iptal edilmemiş" süzgeci yok sayılır (iptal edilmiş örnek kayıtlar da silinir).
/// </summary>
public sealed class ExampleClinicDataCleaner : IExampleClinicCleaner
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IFileStorage _fileStorage;
    private readonly IClientIntegrationEventOutbox _clientEvents;
    private readonly IPetIntegrationEventOutbox _petEvents;
    private readonly IAppointmentIntegrationEventOutbox _appointmentEvents;
    private readonly IAppointmentProjectionSnapshotFactory _appointmentSnapshots;
    private readonly IPaymentIntegrationEventOutbox _paymentEvents;
    private readonly ILogger<ExampleClinicDataCleaner> _logger;

    public ExampleClinicDataCleaner(
        AppDbContext db,
        TimeProvider timeProvider,
        IFileStorage fileStorage,
        IClientIntegrationEventOutbox clientEvents,
        IPetIntegrationEventOutbox petEvents,
        IAppointmentIntegrationEventOutbox appointmentEvents,
        IAppointmentProjectionSnapshotFactory appointmentSnapshots,
        IPaymentIntegrationEventOutbox paymentEvents,
        ILogger<ExampleClinicDataCleaner> logger)
    {
        _db = db;
        _timeProvider = timeProvider;
        _fileStorage = fileStorage;
        _clientEvents = clientEvents;
        _petEvents = petEvents;
        _appointmentEvents = appointmentEvents;
        _appointmentSnapshots = appointmentSnapshots;
        _paymentEvents = paymentEvents;
        _logger = logger;
    }

    public async Task<ExampleClinicStatusDto> GetStatusAsync(Guid tenantId, CancellationToken ct)
    {
        var scope = await ScopeAsync(tenantId, ct);
        if (scope.IsEmpty)
            return new ExampleClinicStatusDto(false, ExampleClinicDataCountsDto.Empty);

        var other = 0;
        foreach (var count in scope.OtherRecordCounts())
            other += await count(ct);

        return new ExampleClinicStatusDto(true, new ExampleClinicDataCountsDto(
            scope.ClientIds.Length,
            scope.PetIds.Length,
            await scope.Appointments.CountAsync(ct),
            await scope.Examinations.CountAsync(ct),
            await scope.Vaccinations.CountAsync(ct),
            await scope.Treatments.CountAsync(ct),
            await scope.LabResults.CountAsync(ct),
            await scope.Charges.CountAsync(ct),
            await scope.Payments.CountAsync(ct),
            scope.DeletableProductIds.Length,
            scope.DeactivatableProductIds.Length,
            await scope.ProductStockMovements.CountAsync(ct),
            other));
    }

    public async Task<ExampleClinicDataCountsDto> ClearAsync(Guid tenantId, CancellationToken ct)
    {
        var scope = await ScopeAsync(tenantId, ct);
        if (scope.IsEmpty)
            return ExampleClinicDataCountsDto.Empty;

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await EnqueueReadModelEventsAsync(scope, nowUtc, ct);
        var storageKeys = await scope.StoredFiles.Select(f => f.StorageKey).ToListAsync(ct);

        // Sıra: önce bağımlılar (alt sorgular ebeveyn silinmeden çalışır), en son müşteri ve ürün.
        var other = 0;
        await scope.ReminderLogs.ExecuteDeleteAsync(ct);
        await scope.StoredFiles.ExecuteDeleteAsync(ct);
        await scope.PaymentAllocations.ExecuteDeleteAsync(ct);
        await scope.SaleLines.ExecuteDeleteAsync(ct);
        other += await scope.Sales.ExecuteDeleteAsync(ct);
        other += await scope.ChargeSuggestions.ExecuteDeleteAsync(ct);
        other += await scope.Administrations.ExecuteDeleteAsync(ct);
        other += await scope.CareInstructions.ExecuteDeleteAsync(ct);
        await scope.PrescriptionItems.ExecuteDeleteAsync(ct);
        other += await scope.Prescriptions.ExecuteDeleteAsync(ct);
        var payments = await scope.Payments.ExecuteDeleteAsync(ct);
        var charges = await scope.Charges.ExecuteDeleteAsync(ct);
        var treatments = await scope.Treatments.ExecuteDeleteAsync(ct);
        var labResults = await scope.LabResults.ExecuteDeleteAsync(ct);
        other += await scope.ImagingRecords.ExecuteDeleteAsync(ct);
        other += await scope.Hospitalizations.ExecuteDeleteAsync(ct);
        var vaccinations = await scope.Vaccinations.ExecuteDeleteAsync(ct);
        other += await scope.ExaminationAddenda.ExecuteDeleteAsync(ct);
        var examinations = await scope.Examinations.ExecuteDeleteAsync(ct);
        other += await scope.Visits.ExecuteDeleteAsync(ct);
        var appointments = await scope.Appointments.ExecuteDeleteAsync(ct);
        other += await scope.OwnerPortalInvites.ExecuteDeleteAsync(ct);
        other += await scope.OwnerPortalLinks.ExecuteDeleteAsync(ct);
        var pets = await scope.Pets.ExecuteDeleteAsync(ct);
        var clients = await scope.Clients.ExecuteDeleteAsync(ct);

        var stockMovements = await scope.ProductStockMovements.ExecuteDeleteAsync(ct);
        await scope.ProductBatches.ExecuteDeleteAsync(ct);
        await scope.ProductStocks.ExecuteDeleteAsync(ct);
        var products = await scope.DeletableProducts.ExecuteDeleteAsync(ct);
        var deactivated = await DeactivateUsedProductsAsync(scope, ct);

        await DeleteFileContentsAsync(storageKeys, ct);

        var result = new ExampleClinicDataCountsDto(
            clients, pets, appointments, examinations, vaccinations, treatments, labResults, charges, payments,
            products, deactivated, stockMovements, other);

        _logger.LogInformation(
            "Örnek klinik temizlendi. TenantId={TenantId} Clients={Clients} Pets={Pets} Appointments={Appointments} Examinations={Examinations} Payments={Payments} Products={Products} ProductsDeactivated={ProductsDeactivated} Other={Other}",
            tenantId, clients, pets, appointments, examinations, payments, products, deactivated, other);

        return result;
    }

    /// <summary>
    /// Okuma modelleri: müşteri/hasta silme, randevu silme (anlık görüntü silmeden önce alınır) ve ödeme iptali ("hiç
    /// alınmamış"; zaten iptal edilmiş ödemenin satırı yoktur). SaveChanges tamponu outbox'a yazar.
    /// </summary>
    private async Task EnqueueReadModelEventsAsync(Scope scope, DateTime nowUtc, CancellationToken ct)
    {
        var appointments = await scope.Appointments.AsNoTracking().ToListAsync(ct);
        foreach (var appointment in appointments)
        {
            var sequence = appointment.AdvanceMutationSequence();
            var snapshot = await _appointmentSnapshots.CreateAsync(appointment, ct);
            await _appointmentEvents.EnqueueAsync(
                AppointmentIntegrationEventTypes.Deleted,
                new AppointmentDeletedIntegrationEvent(Guid.NewGuid(), nowUtc, sequence, snapshot),
                ct);
        }

        var payments = await scope.Payments.AsNoTracking().Where(p => p.VoidedAtUtc == null).ToListAsync(ct);
        if (payments.Count > 0)
        {
            var clientIds = payments.Select(p => p.ClientId).Distinct().ToArray();
            var petIds = payments.Where(p => p.PetId != null).Select(p => p.PetId!.Value).Distinct().ToArray();
            var clinicIds = payments.Select(p => p.ClinicId).Distinct().ToArray();
            var clientNames = await _db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.FullName, ct);
            var petNames = await _db.Pets.AsNoTracking().Where(p => petIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
            var clinicNames = await _db.Clinics.AsNoTracking().Where(c => clinicIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            foreach (var payment in payments)
            {
                var snapshot = PaymentProjectionSnapshotFactory.Create(
                    payment,
                    clientNames[payment.ClientId],
                    clinicNames[payment.ClinicId],
                    payment.PetId is { } petId && petNames.TryGetValue(petId, out var petName) ? petName : null);
                await _paymentEvents.EnqueueAsync(
                    PaymentIntegrationEventTypes.Voided,
                    new PaymentVoidedIntegrationEvent(Guid.NewGuid(), nowUtc, snapshot),
                    ct);
            }
        }

        foreach (var petId in scope.PetIds)
        {
            await _petEvents.EnqueueAsync(
                PetIntegrationEventTypes.Deleted,
                new PetDeletedIntegrationEvent(Guid.NewGuid(), nowUtc, petId, scope.TenantId),
                ct);
        }

        foreach (var clientId in scope.ClientIds)
        {
            await _clientEvents.EnqueueAsync(
                ClientIntegrationEventTypes.Deleted,
                new ClientDeletedIntegrationEvent(Guid.NewGuid(), nowUtc, clientId, scope.TenantId),
                ct);
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<int> DeactivateUsedProductsAsync(Scope scope, CancellationToken ct)
    {
        if (scope.DeactivatableProductIds.Length == 0)
            return 0;

        var ids = scope.DeactivatableProductIds;
        var products = await _db.Products.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        foreach (var product in products)
            product.Deactivate();

        await _db.SaveChangesAsync(ct);
        return products.Count;
    }

    /// <summary>Disk içeriği en iyi çabayla: kayıtlar silindi, içerik silinemezse yalnız günlüğe yazılır (işlem bozulmaz).</summary>
    private async Task DeleteFileContentsAsync(IReadOnlyList<string> storageKeys, CancellationToken ct)
    {
        foreach (var key in storageKeys)
        {
            try
            {
                await _fileStorage.DeleteAsync(key, ct);
            }
            catch (Exception ex) when (ex is FileStorageUnavailableException or IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("Örnek klinik dosya içeriği silinemedi. StorageKey={StorageKey} Exception={ExceptionType}", key, ex.GetType().FullName);
            }
        }
    }

    private async Task<Scope> ScopeAsync(Guid tenantId, CancellationToken ct)
    {
        var tag = ExampleClinicDataGenerator.TagPrefix;
        var clientIds = await _db.Clients.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Address != null && c.Address.StartsWith(tag))
            .Select(c => c.Id)
            .ToArrayAsync(ct);
        var petIds = await _db.Pets.AsNoTracking()
            .Where(p => p.TenantId == tenantId && (clientIds.Contains(p.ClientId) || (p.Notes != null && p.Notes.StartsWith(tag))))
            .Select(p => p.Id)
            .ToArrayAsync(ct);

        var scope = new Scope(_db, tenantId, clientIds, petIds);
        var candidates = await _db.Products.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.Description != null && p.Description.StartsWith(tag))
            .Select(p => new { p.Id, p.IsActive })
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return scope;

        var candidateIds = candidates.Select(p => p.Id).ToArray();
        var used = (await scope.ProductUsages(candidateIds).ToListAsync(ct)).ToHashSet();
        scope.DeletableProductIds = candidates.Where(p => !used.Contains(p.Id)).Select(p => p.Id).ToArray();
        scope.DeactivatableProductIds = candidates.Where(p => used.Contains(p.Id) && p.IsActive).Select(p => p.Id).ToArray();
        return scope;
    }

    /// <summary>Örnek kümeye düşen kayıtların sorguları; sayım ve silme aynı tanımı kullanır.</summary>
    private sealed class Scope(AppDbContext db, Guid tenantId, Guid[] clientIds, Guid[] petIds)
    {
        private readonly string _tag = ExampleClinicDataGenerator.TagPrefix;

        public Guid TenantId { get; } = tenantId;
        public Guid[] ClientIds { get; } = clientIds;
        public Guid[] PetIds { get; } = petIds;
        public Guid[] DeletableProductIds { get; set; } = [];
        public Guid[] DeactivatableProductIds { get; set; } = [];

        public bool IsEmpty => ClientIds.Length == 0 && PetIds.Length == 0
            && DeletableProductIds.Length == 0 && DeactivatableProductIds.Length == 0;

        public IQueryable<Client> Clients => db.Clients.Where(x => x.TenantId == TenantId && ClientIds.Contains(x.Id));
        public IQueryable<Pet> Pets => db.Pets.Where(x => x.TenantId == TenantId && PetIds.Contains(x.Id));
        public IQueryable<Appointment> Appointments => db.Appointments.Where(x => x.TenantId == TenantId && PetIds.Contains(x.PetId));
        public IQueryable<Visit> Visits => db.Visits.Where(x => x.TenantId == TenantId && PetIds.Contains(x.PetId));
        public IQueryable<Examination> Examinations => db.Examinations.Where(x => x.TenantId == TenantId && PetIds.Contains(x.PetId));
        public IQueryable<ExaminationAddendum> ExaminationAddenda
            => db.ExaminationAddenda.Where(x => Examinations.Select(e => e.Id).Contains(x.ExaminationId));
        public IQueryable<Vaccination> Vaccinations => db.Vaccinations.Where(x => x.TenantId == TenantId && PetIds.Contains(x.PetId));
        public IQueryable<Treatment> Treatments => db.Treatments.Where(x => x.TenantId == TenantId && PetIds.Contains(x.PetId));
        public IQueryable<LabResult> LabResults => db.LabResults.Where(x => x.TenantId == TenantId && PetIds.Contains(x.PetId));
        public IQueryable<ImagingRecord> ImagingRecords => db.ImagingRecords.Where(x => x.TenantId == TenantId && PetIds.Contains(x.PetId));
        public IQueryable<Hospitalization> Hospitalizations => db.Hospitalizations.Where(x => x.TenantId == TenantId && PetIds.Contains(x.PetId));
        public IQueryable<CareInstruction> CareInstructions => db.CareInstructions.Where(x => x.TenantId == TenantId && PetIds.Contains(x.PetId));
        public IQueryable<Administration> Administrations => db.Administrations.Where(x => x.TenantId == TenantId && PetIds.Contains(x.PetId));
        public IQueryable<ChargeSuggestion> ChargeSuggestions => db.ChargeSuggestions.Where(x => x.TenantId == TenantId && PetIds.Contains(x.PetId));
        public IQueryable<Prescription> Prescriptions => db.Prescriptions.Where(x => x.TenantId == TenantId && PetIds.Contains(x.PetId));
        public IQueryable<PrescriptionItem> PrescriptionItems
            => db.Set<PrescriptionItem>().Where(x => Prescriptions.Select(p => p.Id).Contains(x.PrescriptionId));
        public IQueryable<Charge> Charges => db.Charges.IgnoreQueryFilters()
            .Where(x => x.TenantId == TenantId && (ClientIds.Contains(x.ClientId) || (x.PetId != null && PetIds.Contains(x.PetId.Value))));
        public IQueryable<Payment> Payments => db.Payments.IgnoreQueryFilters()
            .Where(x => x.TenantId == TenantId && (ClientIds.Contains(x.ClientId) || (x.PetId != null && PetIds.Contains(x.PetId.Value))));
        public IQueryable<PaymentAllocation> PaymentAllocations => db.PaymentAllocations.IgnoreQueryFilters()
            .Where(x => Payments.Select(p => p.Id).Contains(x.PaymentId) || Charges.Select(c => c.Id).Contains(x.ChargeId));
        public IQueryable<Sale> Sales => db.Sales.Where(x => x.TenantId == TenantId && ClientIds.Contains(x.ClientId));
        public IQueryable<SaleLine> SaleLines => db.SaleLines.Where(x => Sales.Select(s => s.Id).Contains(x.SaleId));
        public IQueryable<OwnerPortalInvite> OwnerPortalInvites => db.OwnerPortalInvites.Where(x => ClientIds.Contains(x.ClientId));
        public IQueryable<OwnerAccountClientLink> OwnerPortalLinks => db.OwnerAccountClientLinks.Where(x => ClientIds.Contains(x.ClientId));
        public IQueryable<StoredFile> StoredFiles => db.StoredFiles.Where(x => x.TenantId == TenantId
            && ((x.OwnerType == StoredFileOwnerType.ImagingRecord && ImagingRecords.Select(i => i.Id).Contains(x.OwnerId))
                || (x.OwnerType == StoredFileOwnerType.LabResult && LabResults.Select(l => l.Id).Contains(x.OwnerId))));
        public IQueryable<ReminderDispatchLog> ReminderLogs => db.ReminderDispatchLogs.Where(x => x.TenantId == TenantId
            && ((x.SourceEntityType == ReminderSourceEntityType.Appointment && Appointments.Select(a => a.Id).Contains(x.SourceEntityId))
                || (x.SourceEntityType == ReminderSourceEntityType.Vaccination && Vaccinations.Select(v => v.Id).Contains(x.SourceEntityId))));

        public IQueryable<Product> DeletableProducts => db.Products.Where(x => x.TenantId == TenantId && DeletableProductIds.Contains(x.Id));
        public IQueryable<StockMovement> ProductStockMovements => db.StockMovements.Where(x => x.TenantId == TenantId && DeletableProductIds.Contains(x.ProductId));
        public IQueryable<StockBatch> ProductBatches => db.StockBatches.Where(x => x.TenantId == TenantId && DeletableProductIds.Contains(x.ProductId));
        public IQueryable<ProductStock> ProductStocks => db.ProductStocks.Where(x => x.TenantId == TenantId && DeletableProductIds.Contains(x.ProductId));

        /// <summary>Örnek ürünü kullanan ve silinmeyecek kayıtlar (kullanılan ürün kimlikleri).</summary>
        public IQueryable<Guid> ProductUsages(Guid[] productIds)
            => db.SaleLines.Where(x => productIds.Contains(x.ProductId) && !Sales.Select(s => s.Id).Contains(x.SaleId)).Select(x => x.ProductId)
                .Concat(db.Administrations.Where(x => productIds.Contains(x.ProductId) && !PetIds.Contains(x.PetId)).Select(x => x.ProductId))
                .Concat(db.ChargeSuggestions.Where(x => productIds.Contains(x.ProductId) && !PetIds.Contains(x.PetId)).Select(x => x.ProductId))
                .Concat(db.ExaminationTemplateLines.Where(x => productIds.Contains(x.ProductId)).Select(x => x.ProductId))
                .Concat(db.Set<PrescriptionItem>()
                    .Where(x => x.ProductId != null && productIds.Contains(x.ProductId.Value) && !Prescriptions.Select(p => p.Id).Contains(x.PrescriptionId))
                    .Select(x => x.ProductId!.Value))
                .Concat(db.StockMovements.Where(x => productIds.Contains(x.ProductId) && (x.Notes == null || !x.Notes.StartsWith(_tag))).Select(x => x.ProductId))
                .Distinct();

        /// <summary>Yanıtta "diğer" toplamına giren bağlı kayıt sayımları (silme sırasındaki <c>other</c> ile aynı küme).</summary>
        public IEnumerable<Func<CancellationToken, Task<int>>> OtherRecordCounts()
        {
            yield return ct => Sales.CountAsync(ct);
            yield return ct => ChargeSuggestions.CountAsync(ct);
            yield return ct => Administrations.CountAsync(ct);
            yield return ct => CareInstructions.CountAsync(ct);
            yield return ct => Prescriptions.CountAsync(ct);
            yield return ct => ImagingRecords.CountAsync(ct);
            yield return ct => Hospitalizations.CountAsync(ct);
            yield return ct => ExaminationAddenda.CountAsync(ct);
            yield return ct => Visits.CountAsync(ct);
            yield return ct => OwnerPortalInvites.CountAsync(ct);
            yield return ct => OwnerPortalLinks.CountAsync(ct);
        }
    }
}
