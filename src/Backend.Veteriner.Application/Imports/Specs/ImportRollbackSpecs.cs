using Ardalis.Specification;
using Backend.Veteriner.Application.Imports.Rollback;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ClientCredits;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Imports.Specs;

/// <summary>
/// Geri alma ile ilgili satırlar (hedef kayıt kimliğiyle, izlenmeyen projeksiyon): partinin oluşturduğu (<c>Created</c>),
/// önceki kısmi geri almada kalan (<c>Kept</c>), geri alınmış (<c>RolledBack</c>) ve eşleşen (<c>Matched</c>) satırlar.
/// <paramref name="rowIds"/> verilirse yalnız o satırlar (birim).
/// </summary>
public sealed class ImportRollbackRowsSpec : Specification<ImportBatchRow, ImportRollbackRowInfo>
{
    private static readonly ImportRowApplyStatus[] Relevant =
    [
        ImportRowApplyStatus.Created, ImportRowApplyStatus.Kept, ImportRowApplyStatus.RolledBack, ImportRowApplyStatus.Matched,
    ];

    public ImportRollbackRowsSpec(Guid batchId, IReadOnlyCollection<Guid>? rowIds = null)
    {
        Query.AsNoTracking();
        Query.Where(r => r.BatchId == batchId && Relevant.Contains(r.ApplyStatus));
        if (rowIds is not null)
        {
            var ids = rowIds.ToArray();
            Query.Where(r => ids.Contains(r.Id));
        }

        Query.Select(r => new ImportRollbackRowInfo(r.Id, r.Table, r.RowNumber, r.ApplyStatus, r.TargetEntityId));
    }
}

/// <summary>Birimin geri alma hedefi satırları (izlenir; sonuç yazmak için).</summary>
public sealed class ImportRollbackTargetRowsSpec : Specification<ImportBatchRow>
{
    public ImportRollbackTargetRowsSpec(Guid batchId, IReadOnlyCollection<Guid> rowIds)
    {
        var ids = rowIds.ToArray();
        Query.Where(r => r.BatchId == batchId
                         && ids.Contains(r.Id)
                         && (r.ApplyStatus == ImportRowApplyStatus.Created || r.ApplyStatus == ImportRowApplyStatus.Kept));
    }
}

/// <summary>Geri almada silinecek aşılar (izlenir).</summary>
public sealed class ImportRollbackVaccinationsSpec : Specification<Vaccination>
{
    public ImportRollbackVaccinationsSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        var set = ids.ToArray();
        Query.Where(v => v.TenantId == tenantId && set.Contains(v.Id));
    }
}

/// <summary>Geri almada silinecek hastalar (izlenir).</summary>
public sealed class ImportRollbackPetsSpec : Specification<Pet>
{
    public ImportRollbackPetsSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        var set = ids.ToArray();
        Query.Where(p => p.TenantId == tenantId && set.Contains(p.Id));
    }
}

/// <summary>Geri almada silinecek müşteriler (izlenir).</summary>
public sealed class ImportRollbackClientsSpec : Specification<Client>
{
    public ImportRollbackClientsSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        var set = ids.ToArray();
        Query.Where(c => c.TenantId == tenantId && set.Contains(c.Id));
    }
}

/// <summary>Geri almada silinecek klinik geçmiş kayıtları (izlenir; ADR-048).</summary>
public sealed class ImportRollbackClinicalRecordsSpec : Specification<ImportedClinicalRecord>
{
    public ImportRollbackClinicalRecordsSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        var set = ids.ToArray();
        Query.Where(r => r.TenantId == tenantId && set.Contains(r.Id));
    }
}

/// <summary>Geri almada silinecek hesap hareketleri (izlenir; ADR-048).</summary>
public sealed class ImportRollbackAccountEntriesSpec : Specification<ImportedAccountEntry>
{
    public ImportRollbackAccountEntriesSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        var set = ids.ToArray();
        Query.Where(e => e.TenantId == tenantId && set.Contains(e.Id));
    }
}

/// <summary>Geri almada silinecek randevular (izlenir; ADR-048).</summary>
public sealed class ImportRollbackAppointmentsSpec : Specification<Appointment>
{
    public ImportRollbackAppointmentsSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        var set = ids.ToArray();
        Query.Where(a => a.TenantId == tenantId && set.Contains(a.Id));
    }
}

/// <summary>Geri almada silinecek, partinin açtığı devir ücretleri (izlenir; ADR-048 T3).</summary>
public sealed class ImportRollbackOpeningBalancesSpec : Specification<Charge>
{
    public ImportRollbackOpeningBalancesSpec(Guid tenantId, Guid batchId, IReadOnlyCollection<Guid> ids)
    {
        var set = ids.ToArray();
        Query.Where(c => c.TenantId == tenantId && c.ImportBatchId == batchId && set.Contains(c.Id));
    }
}

/// <summary>Geri almada silinecek, partinin açtığı devir alacakları (izlenir; ADR-048 T3).</summary>
public sealed class ImportRollbackOpeningCreditsSpec : Specification<ClientCredit>
{
    public ImportRollbackOpeningCreditsSpec(Guid tenantId, Guid batchId, IReadOnlyCollection<Guid> ids)
    {
        var set = ids.ToArray();
        Query.Where(c => c.TenantId == tenantId && c.ImportBatchId == batchId && set.Contains(c.Id));
    }
}

/// <summary>Geri almada silinecek ürünler (izlenir; ADR-048 T7).</summary>
public sealed class ImportRollbackProductsSpec : Specification<Product>
{
    public ImportRollbackProductsSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        var set = ids.ToArray();
        Query.Where(p => p.TenantId == tenantId && set.Contains(p.Id));
    }
}

/// <summary>Geri alma ilerlemesi: verilen uygulama durumlarındaki satır sayısı.</summary>
public sealed class ImportRowsByApplyStatusSpec : Specification<ImportBatchRow>
{
    public ImportRowsByApplyStatusSpec(Guid batchId, params ImportRowApplyStatus[] statuses)
    {
        Query.AsNoTracking();
        Query.Where(r => r.BatchId == batchId && statuses.Contains(r.ApplyStatus));
    }
}
