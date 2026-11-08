namespace Backend.Veteriner.Application.Clinics.Contracts.Dtos;

/// <summary>
/// <see cref="OwnerCancelLinkEnabled"/> ve <see cref="WaitlistAutoOfferEnabled"/> gönderilmezse mevcut değer korunur (eski istemciler;
/// TIER-019, TIER-018).
/// </summary>
public sealed record UpdateClinicAppointmentSettingsRequest(
    int DefaultAppointmentDurationMinutes,
    int SlotIntervalMinutes,
    bool AllowOverlappingAppointments,
    bool? OwnerCancelLinkEnabled = null,
    bool? WaitlistAutoOfferEnabled = null);
