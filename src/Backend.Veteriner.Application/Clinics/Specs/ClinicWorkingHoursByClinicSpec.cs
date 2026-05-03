using Ardalis.Specification;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics.Specs;

public sealed class ClinicWorkingHoursByClinicSpec : Specification<ClinicWorkingHour>
{
    public ClinicWorkingHoursByClinicSpec(Guid tenantId, Guid clinicId)
    {
        Query.Where(x => x.TenantId == tenantId && x.ClinicId == clinicId)
            .OrderBy(x => x.DayOfWeek);
    }
}
