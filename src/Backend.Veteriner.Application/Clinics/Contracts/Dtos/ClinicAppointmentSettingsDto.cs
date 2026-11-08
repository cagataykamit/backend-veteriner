namespace Backend.Veteriner.Application.Clinics.Contracts.Dtos;

/// <summary><see cref="OwnerCancelLinkEnabled"/>: randevu hatırlatmasında "iptal et" bağlantısı (TIER-019).</summary>
public sealed record ClinicAppointmentSettingsDto(
    int DefaultAppointmentDurationMinutes,
    int SlotIntervalMinutes,
    bool AllowOverlappingAppointments,
    bool OwnerCancelLinkEnabled);
