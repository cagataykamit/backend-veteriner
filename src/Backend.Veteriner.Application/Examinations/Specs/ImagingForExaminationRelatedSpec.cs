using Ardalis.Specification;
using Backend.Veteriner.Domain.Imaging;

namespace Backend.Veteriner.Application.Examinations.Specs;

/// <summary>Muayeneye bağlı görüntüleme kayıtları (tüm durumlar; durum öğede taşınır).</summary>
public sealed class ImagingForExaminationRelatedSpec : Specification<ImagingRecord>
{
    public ImagingForExaminationRelatedSpec(Guid tenantId, Guid? clinicId, Guid examinationId, int take)
    {
        Query.Where(i => i.TenantId == tenantId && i.ExaminationId == examinationId);
        if (clinicId.HasValue)
            Query.Where(i => i.ClinicId == clinicId.Value);
        Query.OrderByDescending(i => i.PerformedAtUtc)
            .ThenByDescending(i => i.Id)
            .Take(take)
            .AsNoTracking();
    }
}
