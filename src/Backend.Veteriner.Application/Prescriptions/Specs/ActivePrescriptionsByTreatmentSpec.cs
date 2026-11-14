using Ardalis.Specification;
using Backend.Veteriner.Domain.Prescriptions;

namespace Backend.Veteriner.Application.Prescriptions.Specs;

/// <summary>Tedaviye bağlı iptal edilmemiş reçeteler (tedavi iptali öncesi denetim, ADR-043 AK-2).</summary>
public sealed class ActivePrescriptionsByTreatmentSpec : Specification<Prescription>
{
    public ActivePrescriptionsByTreatmentSpec(Guid tenantId, Guid treatmentId)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId && p.TreatmentId == treatmentId && p.CancelledAtUtc == null);
    }
}
