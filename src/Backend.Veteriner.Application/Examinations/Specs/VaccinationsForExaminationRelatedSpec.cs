using Ardalis.Specification;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Examinations.Specs;

/// <summary>Muayeneye bağlı aşı kayıtları (tüm durumlar; durum öğede taşınır).</summary>
public sealed class VaccinationsForExaminationRelatedSpec : Specification<Vaccination>
{
    public VaccinationsForExaminationRelatedSpec(Guid tenantId, Guid? clinicId, Guid examinationId, int take)
    {
        Query.Where(v => v.TenantId == tenantId && v.ExaminationId == examinationId);
        if (clinicId.HasValue)
            Query.Where(v => v.ClinicId == clinicId.Value);
        Query.OrderByDescending(v => v.AppliedAtUtc ?? v.DueAtUtc)
            .ThenByDescending(v => v.Id)
            .Take(take)
            .AsNoTracking();
    }
}
