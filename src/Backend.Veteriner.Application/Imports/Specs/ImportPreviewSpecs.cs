using Ardalis.Specification;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Imports.Specs;

/// <summary>Önizleme toplulaştırması için: sorunu olan satırların <c>IssuesJson</c> değerleri (yalnız tek sütun).</summary>
public sealed class ImportIssuesJsonSpec : Specification<ImportBatchRow, string?>
{
    public ImportIssuesJsonSpec(Guid batchId)
    {
        Query.AsNoTracking();
        Query.Where(r => r.BatchId == batchId && r.IssuesJson != null);
        Query.Select(r => r.IssuesJson);
    }
}

/// <summary>Hatırlatma etkisi için: hatasız aşı satırlarının <c>NormalizedJson</c> değerleri (yalnız tek sütun).</summary>
public sealed class ImportVaccinationNormalizedJsonSpec : Specification<ImportBatchRow, string?>
{
    public ImportVaccinationNormalizedJsonSpec(Guid batchId)
    {
        Query.AsNoTracking();
        Query.Where(r => r.BatchId == batchId
                         && r.Table == ImportTable.Vaccinations
                         && r.ValidationStatus != ImportRowValidationStatus.Error
                         && r.NormalizedJson != null);
        Query.Select(r => r.NormalizedJson);
    }
}

/// <summary>Partinin içe aktardığı uygulanmış aşılar: hasta ve sonraki aşı tarihi (gerçek hatırlatma etkisi).</summary>
public sealed class ImportedVaccinationDueSpec : Specification<Vaccination, ImportedVaccinationDue>
{
    public ImportedVaccinationDueSpec(Guid tenantId, Guid batchId)
    {
        Query.AsNoTracking();
        Query.Where(v => v.TenantId == tenantId && v.ImportBatchId == batchId && v.Status == VaccinationStatus.Applied);
        Query.Select(v => new ImportedVaccinationDue(v.PetId, v.DueAtUtc));
    }
}

/// <summary>Hastaların bugünkü sahibi (tek sorgu; kimlikler tek JSON parametresiyle).</summary>
public sealed class ImportPetOwnersSpec : Specification<Pet, ImportPetOwner>
{
    public ImportPetOwnersSpec(Guid tenantId, IReadOnlyCollection<Guid> petIds)
    {
        var ids = petIds.ToArray();
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId && ids.Contains(p.Id));
        Query.Select(p => new ImportPetOwner(p.Id, p.ClientId));
    }
}

/// <summary>Müşterilerin e-postası (yalnız e-posta var mı bilgisi için).</summary>
public sealed class ImportClientEmailsSpec : Specification<Client, ImportClientEmail>
{
    public ImportClientEmailsSpec(Guid tenantId, IReadOnlyCollection<Guid> clientIds)
    {
        var ids = clientIds.ToArray();
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && ids.Contains(c.Id));
        Query.Select(c => new ImportClientEmail(c.Id, c.Email));
    }
}

/// <summary>
/// Satır listesi süzgeci. <paramref name="validationStatus"/>/<paramref name="applyStatus"/> en çok biri dolu olur;
/// <paramref name="issueCodeFragment"/> <see cref="Validation.ImportIssueJson.CodeFragment"/> çıktısıdır.
/// <paramref name="page"/> ve <paramref name="pageSize"/> verilmezse sayfalama yoktur (toplam sayımı için).
/// </summary>
public sealed class ImportBatchRowsFilteredSpec : Specification<ImportBatchRow>
{
    public ImportBatchRowsFilteredSpec(
        Guid batchId,
        ImportTable? table,
        ImportRowValidationStatus? validationStatus,
        ImportRowApplyStatus? applyStatus,
        string? issueCodeFragment,
        int? page = null,
        int? pageSize = null)
    {
        Query.AsNoTracking();
        Query.Where(r => r.BatchId == batchId);

        if (table is { } t)
            Query.Where(r => r.Table == t);
        if (validationStatus is { } v)
            Query.Where(r => r.ValidationStatus == v);
        if (applyStatus is { } a)
            Query.Where(r => r.ApplyStatus == a);
        if (issueCodeFragment is not null)
            Query.Where(r => r.IssuesJson != null && r.IssuesJson.Contains(issueCodeFragment));

        if (page is { } p && pageSize is { } size)
        {
            Query.OrderBy(r => r.Table).ThenBy(r => r.RowNumber)
                .Skip((p - 1) * size)
                .Take(size);
        }
    }
}

/// <summary>Hata raporu satırları: doğrulaması hatalı veya uygulaması başarısız satırlar, tablo ve satır sırasıyla.</summary>
public sealed class ImportErrorRowsSpec : Specification<ImportBatchRow>
{
    public ImportErrorRowsSpec(Guid batchId)
    {
        Query.AsNoTracking();
        Query.Where(r => r.BatchId == batchId
                         && (r.ValidationStatus == ImportRowValidationStatus.Error
                             || r.ApplyStatus == ImportRowApplyStatus.Failed));
        Query.OrderBy(r => r.Table).ThenBy(r => r.RowNumber);
    }
}
