using Ardalis.Specification;
using Backend.Veteriner.Domain.Administrations;

namespace Backend.Veteriner.Application.Examinations.Specs;

/// <summary>Muayeneye bağlı iptal edilmemiş, aşı kaydına bağlı olmayan uygulamalar (iptaller ilişkili özette gösterilmez; bağlı uygulama aşı olayının parçasıdır).</summary>
public sealed class AdministrationsForExaminationRelatedSpec : Specification<Administration>
{
    public AdministrationsForExaminationRelatedSpec(Guid tenantId, Guid? clinicId, Guid examinationId, int take)
    {
        Query.Where(a => a.TenantId == tenantId && a.ExaminationId == examinationId && a.VoidedAtUtc == null && a.VaccinationId == null);
        if (clinicId.HasValue)
            Query.Where(a => a.ClinicId == clinicId.Value);
        Query.OrderByDescending(a => a.AdministeredAtUtc)
            .ThenByDescending(a => a.Id)
            .Take(take)
            .AsNoTracking();
    }
}
