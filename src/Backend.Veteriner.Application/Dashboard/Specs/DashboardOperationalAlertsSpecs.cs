using Ardalis.Specification;
using Backend.Veteriner.Application.Dashboard;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Dashboard.Specs;

public sealed class DashboardOverdueScheduledAppointmentsCountSpec : Specification<Appointment>
{
    public DashboardOverdueScheduledAppointmentsCountSpec(
        Guid tenantId,
        Guid? clinicId,
        DateTime nowUtc,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null)
    {
        Query.AsNoTracking();
        Query.Where(a =>
            a.TenantId == tenantId
            && a.Status == AppointmentStatus.Scheduled
            && a.ScheduledAtUtc < nowUtc);
        DashboardSpecificationClinicScope.ApplyToAppointment(Query, clinicId, accessibleClinicIds);
    }
}

public sealed class DashboardUpcomingAppointmentsNext24HoursCountSpec : Specification<Appointment>
{
    public DashboardUpcomingAppointmentsNext24HoursCountSpec(
        Guid tenantId,
        Guid? clinicId,
        DateTime nowUtc,
        DateTime next24HoursUtcExclusive,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null)
    {
        Query.AsNoTracking();
        Query.Where(a =>
            a.TenantId == tenantId
            && a.Status == AppointmentStatus.Scheduled
            && a.ScheduledAtUtc >= nowUtc
            && a.ScheduledAtUtc < next24HoursUtcExclusive);
        DashboardSpecificationClinicScope.ApplyToAppointment(Query, clinicId, accessibleClinicIds);
    }
}

public sealed class DashboardOverdueVaccinationsCountSpec : Specification<Vaccination>
{
    public DashboardOverdueVaccinationsCountSpec(
        Guid tenantId,
        Guid? clinicId,
        DateTime nowUtc,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null)
    {
        Query.AsNoTracking();
        Query.Where(v =>
            v.TenantId == tenantId
            && v.Status == VaccinationStatus.Scheduled
            && v.DueAtUtc.HasValue
            && v.DueAtUtc.Value < nowUtc);
        DashboardSpecificationClinicScope.ApplyToVaccination(Query, clinicId, accessibleClinicIds);
    }
}

public sealed class DashboardUpcomingVaccinationsNext7DaysCountSpec : Specification<Vaccination>
{
    public DashboardUpcomingVaccinationsNext7DaysCountSpec(
        Guid tenantId,
        Guid? clinicId,
        DateTime nowUtc,
        DateTime next7DaysUtcExclusive,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null)
    {
        Query.AsNoTracking();
        Query.Where(v =>
            v.TenantId == tenantId
            && v.Status == VaccinationStatus.Scheduled
            && v.DueAtUtc.HasValue
            && v.DueAtUtc.Value >= nowUtc
            && v.DueAtUtc.Value < next7DaysUtcExclusive);
        DashboardSpecificationClinicScope.ApplyToVaccination(Query, clinicId, accessibleClinicIds);
    }
}
