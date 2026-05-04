using Backend.Veteriner.Application.Clinics.Contracts.Dtos;

namespace Backend.Veteriner.Application.Clinics.AppointmentSettings;

public static class ClinicAppointmentSettingsDefaults
{
    public static ClinicAppointmentSettingsDto Build()
        => new(
            DefaultAppointmentDurationMinutes: 30,
            SlotIntervalMinutes: 15,
            AllowOverlappingAppointments: false);
}
