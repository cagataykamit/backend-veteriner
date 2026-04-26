using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Dashboard.Specs;

public sealed class DashboardOverdueScheduledAppointmentsCountSpec : Specification<Appointment>
{
    public DashboardOverdueScheduledAppointmentsCountSpec(Guid tenantId, Guid? clinicId, DateTime nowUtc)
    {
        Query.AsNoTracking();
        Query.Where(a =>
            a.TenantId == tenantId
            && a.Status == AppointmentStatus.Scheduled
            && a.ScheduledAtUtc < nowUtc);
        if (clinicId.HasValue)
            Query.Where(a => a.ClinicId == clinicId.Value);
    }
}

public sealed class DashboardUpcomingAppointmentsNext24HoursCountSpec : Specification<Appointment>
{
    public DashboardUpcomingAppointmentsNext24HoursCountSpec(Guid tenantId, Guid? clinicId, DateTime nowUtc, DateTime next24HoursUtcExclusive)
    {
        Query.AsNoTracking();
        Query.Where(a =>
            a.TenantId == tenantId
            && a.Status == AppointmentStatus.Scheduled
            && a.ScheduledAtUtc >= nowUtc
            && a.ScheduledAtUtc < next24HoursUtcExclusive);
        if (clinicId.HasValue)
            Query.Where(a => a.ClinicId == clinicId.Value);
    }
}

public sealed class DashboardOverdueVaccinationsCountSpec : Specification<Vaccination>
{
    public DashboardOverdueVaccinationsCountSpec(Guid tenantId, Guid? clinicId, DateTime nowUtc)
    {
        Query.AsNoTracking();
        Query.Where(v =>
            v.TenantId == tenantId
            && v.Status == VaccinationStatus.Scheduled
            && v.DueAtUtc.HasValue
            && v.DueAtUtc.Value < nowUtc);
        if (clinicId.HasValue)
            Query.Where(v => v.ClinicId == clinicId.Value);
    }
}

public sealed class DashboardUpcomingVaccinationsNext7DaysCountSpec : Specification<Vaccination>
{
    public DashboardUpcomingVaccinationsNext7DaysCountSpec(Guid tenantId, Guid? clinicId, DateTime nowUtc, DateTime next7DaysUtcExclusive)
    {
        Query.AsNoTracking();
        Query.Where(v =>
            v.TenantId == tenantId
            && v.Status == VaccinationStatus.Scheduled
            && v.DueAtUtc.HasValue
            && v.DueAtUtc.Value >= nowUtc
            && v.DueAtUtc.Value < next7DaysUtcExclusive);
        if (clinicId.HasValue)
            Query.Where(v => v.ClinicId == clinicId.Value);
    }
}
