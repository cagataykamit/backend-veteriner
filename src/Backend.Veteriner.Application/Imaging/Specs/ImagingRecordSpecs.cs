using Ardalis.Specification;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Domain.Imaging;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Imaging.Specs;

public sealed class ImagingRecordByIdSpec : Specification<ImagingRecord>
{
    public ImagingRecordByIdSpec(Guid tenantId, Guid id)
    {
        Query.Where(x => x.TenantId == tenantId && x.Id == id);
    }
}

/// <summary>
/// Güncelleme/iptal için izlenmeyen yükleme: kayıt sonra istemcinin beklenen sürümüyle eklenir, böylece UPDATE koşulundaki
/// rowversion istemcinin gönderdiği değerdir (LabResult deseni).
/// </summary>
public sealed class ImagingRecordForUpdateByIdSpec : Specification<ImagingRecord>
{
    public ImagingRecordForUpdateByIdSpec(Guid tenantId, Guid id)
    {
        Query.AsNoTracking();
        Query.Where(x => x.TenantId == tenantId && x.Id == id);
    }
}

/// <summary>Liste süzgeci (sayım ve sayfa aynı koşulları kullanır). <c>status</c> verilmezse yalnız etkin kayıtlar.</summary>
public sealed record ImagingRecordListFilter(
    Guid TenantId,
    Guid? ClinicId,
    IReadOnlyCollection<Guid>? AccessibleClinicIds,
    Guid? PetId,
    Guid? ExaminationId,
    ImagingModality? Modality,
    ImagingRecordStatus? Status,
    DateTime? DateFromUtc,
    DateTime? DateToUtc,
    string? SearchContainsLikePattern,
    Guid[] SearchPetIds);

internal static class ImagingRecordListFilterExtensions
{
    public static void Apply(this ISpecificationBuilder<ImagingRecord> query, ImagingRecordListFilter f)
    {
        query.Where(x => x.TenantId == f.TenantId);
        if (f.ClinicId.HasValue)
        {
            query.Where(x => x.ClinicId == f.ClinicId.Value);
        }
        else if (f.AccessibleClinicIds is not null)
        {
            var ids = f.AccessibleClinicIds;
            if (ids.Count == 0)
                query.Where(x => false);
            else
                query.Where(x => ids.Contains(x.ClinicId));
        }

        if (f.PetId.HasValue)
            query.Where(x => x.PetId == f.PetId.Value);
        if (f.ExaminationId.HasValue)
            query.Where(x => x.ExaminationId == f.ExaminationId.Value);
        if (f.Modality.HasValue)
            query.Where(x => x.Modality == f.Modality.Value);

        var status = f.Status ?? ImagingRecordStatus.Active;
        query.Where(x => x.Status == status);

        if (f.DateFromUtc.HasValue)
            query.Where(x => x.PerformedAtUtc >= f.DateFromUtc.Value);
        if (f.DateToUtc.HasValue)
            query.Where(x => x.PerformedAtUtc <= f.DateToUtc.Value);

        if (f.SearchContainsLikePattern is { } pat)
        {
            var pids = f.SearchPetIds;
            query.Where(x =>
                (x.Description != null && EF.Functions.Like(x.Description, pat))
                || (x.ClinicalNote != null && EF.Functions.Like(x.ClinicalNote, pat))
                || (pids.Length > 0 && pids.Contains(x.PetId)));
        }
    }
}

public sealed class ImagingRecordsFilteredCountSpec : Specification<ImagingRecord>
{
    public ImagingRecordsFilteredCountSpec(ImagingRecordListFilter filter)
    {
        Query.Apply(filter);
    }
}

public sealed class ImagingRecordsFilteredPagedSpec : Specification<ImagingRecord>
{
    public ImagingRecordsFilteredPagedSpec(
        ImagingRecordListFilter filter,
        int page,
        int pageSize,
        ListSortChoice<ImagingRecordListSortField>? sort)
    {
        Query.AsNoTracking();
        Query.Apply(filter);

        // Tek izinli alan PerformedAtUtc; sort yoksa azalan (bkz. ImagingRecordListSort).
        var ordered = (sort?.Descending ?? true)
            ? Query.OrderByDescending(x => x.PerformedAtUtc).ThenByDescending(x => x.Id)
            : Query.OrderBy(x => x.PerformedAtUtc).ThenBy(x => x.Id);

        ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
