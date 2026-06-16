using Backend.Veteriner.Application.Appointments.Contracts.Dtos;

namespace Backend.Veteriner.Application.Appointments.ReadModels;

public sealed record AppointmentListReadResult(
    IReadOnlyList<AppointmentListItemDto> Items,
    int TotalCount);
