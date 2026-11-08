using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics.AppointmentSettings;

public static class ClinicAppointmentSettingsDefaults
{
    public static ClinicAppointmentSettingsDto Build()
        => new(
            DefaultAppointmentDurationMinutes: 30,
            SlotIntervalMinutes: 15,
            AllowOverlappingAppointments: false,
            OwnerCancelLinkEnabled: ClinicAppointmentSettings.DefaultOwnerCancelLinkEnabled,
            WaitlistAutoOfferEnabled: ClinicAppointmentSettings.DefaultWaitlistAutoOfferEnabled);

    public static ClinicAppointmentSettingsDto ToDto(ClinicAppointmentSettings row)
        => new(
            row.DefaultAppointmentDurationMinutes,
            row.SlotIntervalMinutes,
            row.AllowOverlappingAppointments,
            row.OwnerCancelLinkEnabled,
            row.WaitlistAutoOfferEnabled);
}
