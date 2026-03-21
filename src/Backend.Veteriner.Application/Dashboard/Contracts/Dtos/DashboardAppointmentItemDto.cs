using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Dashboard.Contracts.Dtos;

public sealed record DashboardAppointmentItemDto(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    DateTime ScheduledAtUtc,
    AppointmentStatus Status);
