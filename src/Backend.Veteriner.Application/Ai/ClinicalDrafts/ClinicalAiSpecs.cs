using Ardalis.Specification;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Treatments;

namespace Backend.Veteriner.Application.Ai.ClinicalDrafts;

/// <summary>Muayeneye bağlı reçeteler, kalemleriyle; iptaller hariç, en yeni önce.</summary>
public sealed class PrescriptionsForExaminationAiSpec : Specification<Prescription>
{
    public PrescriptionsForExaminationAiSpec(Guid tenantId, Guid clinicId, Guid examinationId, int take)
        => Query
            .Where(p => p.TenantId == tenantId && p.ClinicId == clinicId && p.ExaminationId == examinationId && p.CancelledAtUtc == null)
            .Include(p => p.Items)
            .OrderByDescending(p => p.PrescribedAtUtc).ThenByDescending(p => p.Id)
            .Take(take);
}

/// <summary>Muayeneye bağlı tedaviler; iptaller hariç, en yeni önce.</summary>
public sealed class TreatmentsForExaminationAiSpec : Specification<Treatment>
{
    public TreatmentsForExaminationAiSpec(Guid tenantId, Guid clinicId, Guid examinationId, int take)
        => Query
            .Where(t => t.TenantId == tenantId && t.ClinicId == clinicId && t.ExaminationId == examinationId && t.CancelledAtUtc == null)
            .OrderByDescending(t => t.TreatmentDateUtc).ThenByDescending(t => t.Id)
            .Take(take);
}
