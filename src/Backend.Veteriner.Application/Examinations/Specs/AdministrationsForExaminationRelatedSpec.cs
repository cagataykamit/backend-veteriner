using Ardalis.Specification;
using Backend.Veteriner.Domain.Administrations;

namespace Backend.Veteriner.Application.Examinations.Specs;

/// <summary>Muayeneye bağlı iptal edilmemiş uygulamalar (iptaller ilişkili özette gösterilmez).</summary>
public sealed class AdministrationsForExaminationRelatedSpec : Specification<Administration>
{
    public AdministrationsForExaminationRelatedSpec(Guid tenantId, Guid? clinicId, Guid examinationId, int take)
    {
        Query.Where(a => a.TenantId == tenantId && a.ExaminationId == examinationId && a.VoidedAtUtc == null);
        if (clinicId.HasValue)
            Query.Where(a => a.ClinicId == clinicId.Value);
        Query.OrderByDescending(a => a.AdministeredAtUtc)
            .ThenByDescending(a => a.Id)
            .Take(take)
            .AsNoTracking();
    }
}
