using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.Specs;

public sealed record AppointmentCalendarRow(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    DateTime ScheduledAtUtc,
    int DurationMinutes,
    AppointmentStatus Status,
    AppointmentType AppointmentType,
    Guid? VeterinarianUserId);

public sealed class AppointmentsCalendarSpec : Specification<Appointment, AppointmentCalendarRow>
{
    public AppointmentsCalendarSpec(
        Guid tenantId,
        Guid? clinicId,
        DateTime dateFromUtc,
        DateTime dateToUtc,
        AppointmentStatus? status)
    {
        Query.AsNoTracking();
        Query.Where(a =>
            a.TenantId == tenantId
            && a.ScheduledAtUtc >= dateFromUtc
            && a.ScheduledAtUtc < dateToUtc);
        if (clinicId.HasValue)
            Query.Where(a => a.ClinicId == clinicId.Value);
        if (status.HasValue)
            Query.Where(a => a.Status == status.Value);

        Query.OrderBy(a => a.ScheduledAtUtc).ThenBy(a => a.Id);
        Query.Select(a => new AppointmentCalendarRow(
            a.Id,
            a.ClinicId,
            a.PetId,
            a.ScheduledAtUtc,
            a.DurationMinutes,
            a.Status,
            a.AppointmentType,
            a.VeterinarianUserId));
    }
}

/// <summary>Randevuların hekimi (takvimde okuma modeli yolu hekim taşımaz; komut veritabanından tamamlanır).</summary>
public sealed class AppointmentVeterinariansByIdsSpec : Specification<Appointment, AppointmentVeterinarianRow>
{
    public AppointmentVeterinariansByIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> appointmentIds)
    {
        var ids = appointmentIds.ToArray();
        Query.AsNoTracking();
        Query.Where(a => a.TenantId == tenantId && ids.Contains(a.Id) && a.VeterinarianUserId != null);
        Query.Select(a => new AppointmentVeterinarianRow(a.Id, a.VeterinarianUserId!.Value));
    }
}

public sealed record AppointmentVeterinarianRow(Guid AppointmentId, Guid VeterinarianUserId);
