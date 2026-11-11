using Ardalis.Specification;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.Imports.Specs;

// Tam geçiş tablolarının (ADR-048) doğrulamada okunan mevcut verisi; her biri tek sorgu, küme parametre olarak gider.

/// <summary>Bulunan hastaların daha önce içe aktarılmış klinik geçmiş kayıtları (mükerrer kayıt denetimi).</summary>
public sealed class ImportExistingClinicalRecordsSpec : Specification<ImportedClinicalRecord, ImportExistingClinicalRecord>
{
    public ImportExistingClinicalRecordsSpec(Guid tenantId, IReadOnlyCollection<Guid> petIds)
    {
        var petSet = petIds.ToArray();
        Query.AsNoTracking();
        Query.Where(r => r.TenantId == tenantId && petSet.Contains(r.PetId));
        Query.Select(r => new ImportExistingClinicalRecord(
            r.PetId, r.RecordedOn, r.Complaint, r.History, r.Findings, r.Diagnosis, r.Treatment, r.Prescription, r.LabResults, r.Notes));
    }
}

/// <summary>Bulunan müşterilerin daha önce içe aktarılmış hesap hareketleri (mükerrer hareket denetimi).</summary>
public sealed class ImportExistingAccountEntriesSpec : Specification<ImportedAccountEntry, ImportExistingAccountEntry>
{
    public ImportExistingAccountEntriesSpec(Guid tenantId, IReadOnlyCollection<Guid> clientIds)
    {
        var clientSet = clientIds.ToArray();
        Query.AsNoTracking();
        Query.Where(e => e.TenantId == tenantId && clientSet.Contains(e.ClientId));
        Query.Select(e => new ImportExistingAccountEntry(e.ClientId, e.OccurredOn, e.Kind, e.Amount, e.DocumentNumber, e.Description));
    }
}

/// <summary>Bulunan hastaların iptal edilmemiş, <paramref name="fromUtc"/> sonrasındaki randevuları (mükerrer randevu denetimi).</summary>
public sealed class ImportExistingAppointmentsSpec : Specification<Appointment, ImportExistingAppointment>
{
    public ImportExistingAppointmentsSpec(Guid tenantId, IReadOnlyCollection<Guid> petIds, DateTime fromUtc)
    {
        var petSet = petIds.ToArray();
        Query.AsNoTracking();
        Query.Where(a => a.TenantId == tenantId
                         && petSet.Contains(a.PetId)
                         && a.Status != AppointmentStatus.Cancelled
                         && a.ScheduledAtUtc > fromUtc);
        Query.Select(a => new ImportExistingAppointment(a.PetId, a.ScheduledAtUtc));
    }
}

/// <summary>Kiracının tüm ürünleri (barkod ve ad + tür eşleşmesi bellekte; ürün kataloğu küçüktür).</summary>
public sealed class ImportExistingProductsSpec : Specification<Product, ImportExistingProduct>
{
    public ImportExistingProductsSpec(Guid tenantId)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId);
        Query.Select(p => new ImportExistingProduct(p.Id, p.Name, p.Kind, p.Barcode));
    }
}

/// <summary>Kiracının aktif ürün kategorileri (katlanmış ad eşleşmesi).</summary>
public sealed class ImportProductCategoriesSpec : Specification<ProductCategory, ImportCatalogEntry>
{
    public ImportProductCategoriesSpec(Guid tenantId)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && c.IsActive);
        Query.Select(c => new ImportCatalogEntry(c.Id, c.Name, null, null));
    }
}

/// <summary>Kliniğin aktif randevu nedenleri (katlanmış ad eşleşmesi).</summary>
public sealed class ImportAppointmentReasonsSpec : Specification<AppointmentReason, ImportCatalogEntry>
{
    public ImportAppointmentReasonsSpec(Guid tenantId, Guid clinicId)
    {
        Query.AsNoTracking();
        Query.Where(r => r.TenantId == tenantId && r.ClinicId == clinicId && r.IsActive);
        Query.Select(r => new ImportCatalogEntry(r.Id, r.Name, null, null));
    }
}

/// <summary>
/// Uygulama anında ürün yeniden eşleşmesi: aynı tür ve (veritabanı harmanlamasıyla) aynı ad; katlanmış karşılaştırma bellekte
/// yapılır (<c>ImportTextFolding</c>).
/// </summary>
public sealed class ImportProductsByNameSpec : Specification<Product>
{
    public ImportProductsByNameSpec(Guid tenantId, string name, ProductKind kind)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId && p.Kind == kind && p.Name == name);
    }
}
