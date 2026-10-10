using Ardalis.Specification;
using Backend.Veteriner.Domain.Visits;

namespace Backend.Veteriner.Application.Visits.Specs;

/// <summary>Randevuya bağlı, yanlış geliş olmayan geliş (randevu başına en çok bir).</summary>
public sealed class VisitByAppointmentIdSpec : Specification<Visit>
{
    public VisitByAppointmentIdSpec(Guid tenantId, Guid appointmentId)
    {
        Query.Where(v => v.TenantId == tenantId
                         && v.AppointmentId == appointmentId
                         && v.VoidedAtUtc == null)
            .AsNoTracking();
    }
}
