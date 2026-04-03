using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Clients.Contracts.Dtos;

public sealed record ClientRecentSummaryDto(
    Guid ClientId,
    IReadOnlyList<ClientRecentAppointmentSummaryItemDto> RecentAppointments,
    IReadOnlyList<ClientRecentExaminationSummaryItemDto> RecentExaminations);

public sealed record ClientRecentAppointmentSummaryItemDto(
    Guid Id,
    DateTime ScheduledAtUtc,
    Guid PetId,
    string PetName,
    AppointmentStatus Status,
    string? Notes);

public sealed record ClientRecentExaminationSummaryItemDto(
    Guid Id,
    DateTime ExaminedAtUtc,
    Guid PetId,
    string PetName,
    string VisitReason);
