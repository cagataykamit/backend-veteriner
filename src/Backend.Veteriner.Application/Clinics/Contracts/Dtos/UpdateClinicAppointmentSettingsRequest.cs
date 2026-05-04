namespace Backend.Veteriner.Application.Clinics.Contracts.Dtos;

public sealed record UpdateClinicAppointmentSettingsRequest(
    int DefaultAppointmentDurationMinutes,
    int SlotIntervalMinutes,
    bool AllowOverlappingAppointments);
