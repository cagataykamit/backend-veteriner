using Ardalis.Specification;
using Backend.Veteriner.Application.Dashboard;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Dashboard.Specs;

/// <summary>
/// Saati geçmiş, hâlâ planlı randevular. <paramref name="excludedPetIds"/>: vefat eden hayvanların randevuları
/// (otomatik iptal edilmezler, ADR-020 S5 K4; ama gecikmiş iş sayılmazlar).
/// </summary>
public sealed class DashboardOverdueScheduledAppointmentsCountSpec : Specification<Appointment>
{
    public DashboardOverdueScheduledAppointmentsCountSpec(
        Guid tenantId,
        Guid? clinicId,
        DateTime nowUtc,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null,
        IReadOnlyCollection<Guid>? excludedPetIds = null)
    {
        Query.AsNoTracking();
        Query.Where(a =>
            a.TenantId == tenantId
            && a.Status == AppointmentStatus.Scheduled
            && a.ScheduledAtUtc < nowUtc);
        if (excludedPetIds is { Count: > 0 })
            Query.Where(a => !excludedPetIds.Contains(a.PetId));
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
        IReadOnlyCollection<Guid>? accessibleClinicIds = null,
        IReadOnlyCollection<Guid>? excludedPetIds = null)
    {
        Query.AsNoTracking();
        Query.Where(v =>
            v.TenantId == tenantId
            && v.Status == VaccinationStatus.Scheduled
            && v.DueAtUtc.HasValue
            && v.DueAtUtc.Value < nowUtc);
        if (excludedPetIds is { Count: > 0 })
            Query.Where(v => !excludedPetIds.Contains(v.PetId));
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
        IReadOnlyCollection<Guid>? accessibleClinicIds = null,
        IReadOnlyCollection<Guid>? excludedPetIds = null)
    {
        Query.AsNoTracking();
        Query.Where(v =>
            v.TenantId == tenantId
            && v.Status == VaccinationStatus.Scheduled
            && v.DueAtUtc.HasValue
            && v.DueAtUtc.Value >= nowUtc
            && v.DueAtUtc.Value < next7DaysUtcExclusive);
        if (excludedPetIds is { Count: > 0 })
            Query.Where(v => !excludedPetIds.Contains(v.PetId));
        DashboardSpecificationClinicScope.ApplyToVaccination(Query, clinicId, accessibleClinicIds);
    }
}
