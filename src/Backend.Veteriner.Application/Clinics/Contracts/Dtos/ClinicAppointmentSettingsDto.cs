namespace Backend.Veteriner.Application.Clinics.Contracts.Dtos;

public sealed record ClinicAppointmentSettingsDto(
    int DefaultAppointmentDurationMinutes,
    int SlotIntervalMinutes,
    bool AllowOverlappingAppointments);
