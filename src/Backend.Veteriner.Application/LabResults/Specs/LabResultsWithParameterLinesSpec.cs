using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Specifications;
using Backend.Veteriner.Domain.LabResults;

namespace Backend.Veteriner.Application.LabResults.Specs;

/// <summary>
/// Hastanın verilen parametre anahtarlarından en az birini içeren sonuçlanmış lab kayıtları, yalnız o satırlarla;
/// yeniden eskiye (sonuç tarihi, sonra kayıt anı). <paramref name="before"/> verilirse yalnız o kayıttan önceki sonuçlar.
/// </summary>
public sealed class LabResultsWithParameterLinesSpec : Specification<LabResult>
{
    public const int MaxResults = 100;

    public LabResultsWithParameterLinesSpec(
        Guid tenantId,
        Guid petId,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        IReadOnlyCollection<string> parameterKeys,
        (DateTime ResultDateUtc, DateTime CreatedAtUtc)? before = null)
    {
        var keys = parameterKeys.ToArray();
        Query.Where(x => x.TenantId == tenantId && x.PetId == petId && x.Status == LabResultStatus.Resulted);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        if (before is { } b)
        {
            Query.Where(x => x.ResultDateUtc < b.ResultDateUtc
                             || (x.ResultDateUtc == b.ResultDateUtc && x.CreatedAtUtc < b.CreatedAtUtc));
        }

        Query.Where(x => x.Lines.Any(l => keys.Contains(l.ParameterKey)))
            .Include(x => x.Lines.Where(l => keys.Contains(l.ParameterKey)))
            .OrderByDescending(x => x.ResultDateUtc)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Take(MaxResults)
            .AsNoTracking();
    }
}
