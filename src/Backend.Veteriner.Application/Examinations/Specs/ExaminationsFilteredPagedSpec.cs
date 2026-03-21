using Ardalis.Specification;
using Backend.Veteriner.Domain.Examinations;

namespace Backend.Veteriner.Application.Examinations.Specs;

public sealed class ExaminationsFilteredPagedSpec : Specification<Examination>
{
    public ExaminationsFilteredPagedSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid? petId,
        Guid? appointmentId,
        DateTime? dateFromUtc,
        DateTime? dateToUtc,
        int page,
        int pageSize)
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

        Query.OrderByDescending(e => e.ExaminedAtUtc)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
