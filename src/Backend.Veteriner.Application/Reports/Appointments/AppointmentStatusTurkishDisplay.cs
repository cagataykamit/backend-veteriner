using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Reports.Appointments;

/// <summary>CSV/XLSX export için randevu durumu Türkçe etiket (JSON enum sayısal kalır).</summary>
internal static class AppointmentStatusTurkishDisplay
{
    public static string ToLabel(AppointmentStatus status)
        => status switch
        {
            AppointmentStatus.Scheduled => "Planlanmış",
            AppointmentStatus.Completed => "Tamamlandı",
            AppointmentStatus.Cancelled => "İptal",
            _ => status.ToString(),
        };
}
