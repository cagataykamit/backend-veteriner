using Ardalis.Specification;
using Backend.Veteriner.Domain.Examinations;

namespace Backend.Veteriner.Application.Examinations.Specs;

public sealed class ExaminationsFilteredCountSpec : Specification<Examination>
{
    public ExaminationsFilteredCountSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid? petId,
        Guid? appointmentId,
        DateTime? dateFromUtc,
        DateTime? dateToUtc)
    {
        Query.Where(e => e.TenantId == tenantId);
        if (clinicId.HasValue)
            Query.Where(e => e.ClinicId == clinicId.Value);
        if (petId.HasValue)
            Query.Where(e => e.PetId == petId.Value);
        if (appointmentId.HasValue)
            Query.Where(e => e.AppointmentId == appointmentId.Value);
        if (dateFromUtc.HasValue)
            Query.Where(e => e.ExaminedAtUtc >= dateFromUtc.Value);
        if (dateToUtc.HasValue)
            Query.Where(e => e.ExaminedAtUtc <= dateToUtc.Value);
    }
}
