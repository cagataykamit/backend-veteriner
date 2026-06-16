using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.ReadModels;

public sealed record AppointmentListReadRequest(
    AppointmentReadScope Scope,
    Guid? PetId,
    AppointmentStatus? Status,
    DateTime? DateFromUtc,
    DateTime? DateToUtc,
    int Page,
    int PageSize,
    string? SearchContainsLikePattern,
    bool ScheduledAtDescending);
