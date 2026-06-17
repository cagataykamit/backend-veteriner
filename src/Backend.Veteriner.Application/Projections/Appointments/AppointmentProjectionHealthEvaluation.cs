namespace Backend.Veteriner.Application.Projections.Appointments;

public sealed record AppointmentProjectionHealthEvaluation(
    AppointmentProjectionHealthLevel Level,
    string Description,
    IReadOnlyDictionary<string, object?> Data);
