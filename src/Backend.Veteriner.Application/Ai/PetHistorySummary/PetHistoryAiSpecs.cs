using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Specifications;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Ai.PetHistorySummary;

/// <summary>Son reçeteler, kalemleriyle; iptaller hariç.</summary>
public sealed class PrescriptionsForPetAiSpec : Specification<Prescription>
{
    public PrescriptionsForPetAiSpec(Guid tenantId, Guid? clinicId, Guid petId, int take, IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        Query.Where(p => p.TenantId == tenantId && p.PetId == petId && p.CancelledAtUtc == null)
            .Include(p => p.Items);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.OrderByDescending(p => p.PrescribedAtUtc).ThenByDescending(p => p.Id).Take(take);
    }
}

/// <summary>Uygulanmış son aşılar (en yeni önce).</summary>
public sealed class AppliedVaccinationsForPetAiSpec : Specification<Vaccination>
{
    public AppliedVaccinationsForPetAiSpec(Guid tenantId, Guid? clinicId, Guid petId, int take, IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        Query.Where(v => v.TenantId == tenantId && v.PetId == petId && v.Status == VaccinationStatus.Applied);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.OrderByDescending(v => v.AppliedAtUtc).ThenByDescending(v => v.Id).Take(take);
    }
}

/// <summary>Planlanmış aşılar (vadesi en yakın önce; gecikmişler dahil).</summary>
public sealed class ScheduledVaccinationsForPetAiSpec : Specification<Vaccination>
{
    public ScheduledVaccinationsForPetAiSpec(Guid tenantId, Guid? clinicId, Guid petId, int take, IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        Query.Where(v => v.TenantId == tenantId && v.PetId == petId && v.Status == VaccinationStatus.Scheduled);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.OrderBy(v => v.DueAtUtc).ThenBy(v => v.Id).Take(take);
    }
}
