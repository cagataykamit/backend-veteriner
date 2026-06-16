using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.ReadModels;

public sealed record AppointmentCalendarReadRequest(
    AppointmentReadScope Scope,
    DateTime DateFromUtc,
    DateTime DateToUtc,
    AppointmentStatus? Status);
