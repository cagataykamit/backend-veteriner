using Ardalis.Specification;
using Backend.Veteriner.Application.Dashboard;
using Backend.Veteriner.Application.Vaccinations;
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

/// <summary>Gecikmiş aşılar (<see cref="VaccinationDueRule.IsOverdue"/>, ADR-028); <paramref name="excludedPetIds"/>: vefat edenler.</summary>
public sealed class DashboardOverdueVaccinationsCountSpec : Specification<Vaccination>
{
    public DashboardOverdueVaccinationsCountSpec(
        Guid tenantId,
        Guid? clinicId,
        DateTime overdueBeforeUtc,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null,
        IReadOnlyCollection<Guid>? excludedPetIds = null)
    {
        Query.AsNoTracking();
        Query.Where(v => v.TenantId == tenantId);
        Query.Where(VaccinationDueRule.IsOverdue(overdueBeforeUtc));
        if (excludedPetIds is { Count: > 0 })
            Query.Where(v => !excludedPetIds.Contains(v.PetId));
        DashboardSpecificationClinicScope.ApplyToVaccination(Query, clinicId, accessibleClinicIds);
    }
}

/// <summary>Önümüzdeki 7 gün (bugün dahil) içinde vadesi gelen aşılar (<see cref="VaccinationDueRule.IsUpcoming"/>, ADR-028).</summary>
public sealed class DashboardUpcomingVaccinationsNext7DaysCountSpec : Specification<Vaccination>
{
    public DashboardUpcomingVaccinationsNext7DaysCountSpec(
        Guid tenantId,
        Guid? clinicId,
        DateTime fromUtc,
        DateTime toUtcExclusive,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null,
        IReadOnlyCollection<Guid>? excludedPetIds = null)
    {
        Query.AsNoTracking();
        Query.Where(v => v.TenantId == tenantId);
        Query.Where(VaccinationDueRule.IsUpcoming(fromUtc, toUtcExclusive));
        if (excludedPetIds is { Count: > 0 })
            Query.Where(v => !excludedPetIds.Contains(v.PetId));
        DashboardSpecificationClinicScope.ApplyToVaccination(Query, clinicId, accessibleClinicIds);
    }
}
