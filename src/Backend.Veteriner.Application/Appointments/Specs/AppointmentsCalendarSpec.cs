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
    Guid? VeterinarianUserId,
    Guid? ReasonId = null,
    string? Notes = null,
    Guid? RoomResourceId = null,
    Guid? EquipmentResourceId = null);

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
            a.VeterinarianUserId,
            a.ReasonId,
            a.Notes,
            a.RoomResourceId,
            a.EquipmentResourceId));
    }
}

/// <summary>
/// Randevuların hekimi, nedeni, notu, odası ve cihazı: okuma modeli yolu bunları taşımaz; takvim aynı istekte komut veritabanından tamamlar.
/// </summary>
public sealed class AppointmentCalendarDetailsByIdsSpec : Specification<Appointment, AppointmentCalendarDetailsRow>
{
    public AppointmentCalendarDetailsByIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> appointmentIds)
    {
        var ids = appointmentIds.ToArray();
        Query.AsNoTracking();
        Query.Where(a => a.TenantId == tenantId && ids.Contains(a.Id));
        Query.Select(a => new AppointmentCalendarDetailsRow(a.Id, a.VeterinarianUserId, a.ReasonId, a.Notes, a.RoomResourceId, a.EquipmentResourceId));
    }
}

public sealed record AppointmentCalendarDetailsRow(
    Guid AppointmentId,
    Guid? VeterinarianUserId,
    Guid? ReasonId,
    string? Notes,
    Guid? RoomResourceId = null,
    Guid? EquipmentResourceId = null);

/// <summary>Takvimdeki randevu nedenlerinin adı (pasif neden de adıyla görünür).</summary>
public sealed class AppointmentReasonNamesByIdsSpec : Specification<AppointmentReason, AppointmentReasonNameRow>
{
    public AppointmentReasonNamesByIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> reasonIds)
    {
        var ids = reasonIds.ToArray();
        Query.AsNoTracking();
        Query.Where(r => r.TenantId == tenantId && ids.Contains(r.Id));
        Query.Select(r => new AppointmentReasonNameRow(r.Id, r.Name));
    }
}

public sealed record AppointmentReasonNameRow(Guid Id, string Name);
