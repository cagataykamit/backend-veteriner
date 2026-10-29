using Backend.Veteriner.Application.Reports.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.Registers;

namespace Backend.Veteriner.Application.Reports.Appointments;

/// <summary>Randevu raporu PDF başlığı ve sütunları (XLSX ile aynı sütunlar ve sıra; zaman İstanbul).</summary>
internal static class AppointmentsPdfColumns
{
    public const string Title = "Randevu Raporu";

    public static readonly IReadOnlyList<RegisterColumn<AppointmentReportItemDto>> All =
    [
        RegisterColumn<AppointmentReportItemDto>.Time(null, "Randevu Zamanı", r => r.ScheduledAtUtc),
        RegisterColumn<AppointmentReportItemDto>.Text(null, "Klinik", r => r.ClinicName),
        RegisterColumn<AppointmentReportItemDto>.Text(null, "Müşteri", r => r.ClientName),
        RegisterColumn<AppointmentReportItemDto>.Text(null, "Hayvan", r => r.PetName),
        RegisterColumn<AppointmentReportItemDto>.Text(null, "Durum", r => AppointmentStatusTurkishDisplay.ToLabel(r.Status)),
        RegisterColumn<AppointmentReportItemDto>.Text(null, "Not", r => r.Notes),
    ];
}
