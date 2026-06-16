namespace Backend.Veteriner.Application.Appointments.IntegrationEvents;

/// <summary>
/// Appointment read-model projection için denormalize anlık görüntü.
/// Enum değerleri JSON uyumluluğu için açık <see langword="int"/> olarak taşınır.
/// </summary>
public sealed record AppointmentProjectionSnapshot(
    Guid AppointmentId,
    Guid TenantId,
    Guid ClinicId,
    string ClinicName,
    Guid PetId,
    string PetName,
    Guid SpeciesId,
    string SpeciesName,
    Guid ClientId,
    string ClientName,
    string? ClientPhone,
    DateTime ScheduledAtUtc,
    int DurationMinutes,
    int AppointmentType,
    int Status,
    string? Notes);
