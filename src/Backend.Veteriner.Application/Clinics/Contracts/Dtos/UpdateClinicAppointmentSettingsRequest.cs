namespace Backend.Veteriner.Application.Clinics.Contracts.Dtos;

/// <summary><see cref="OwnerCancelLinkEnabled"/> gönderilmezse mevcut değer korunur (eski istemciler; TIER-019).</summary>
public sealed record UpdateClinicAppointmentSettingsRequest(
    int DefaultAppointmentDurationMinutes,
    int SlotIntervalMinutes,
    bool AllowOverlappingAppointments,
    bool? OwnerCancelLinkEnabled = null);
