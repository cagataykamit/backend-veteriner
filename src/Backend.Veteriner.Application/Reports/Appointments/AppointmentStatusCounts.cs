using Backend.Veteriner.Application.Reports.Appointments.Contracts.Dtos;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Reports.Appointments;

/// <summary>Durum bazlı sayım satırları → rapor durum dağılımı; randevu raporu ve işletme performansı aynı kuralı kullanır.</summary>
public static class AppointmentStatusCounts
{
    public static AppointmentReportStatusCountsDto From(IEnumerable<AppointmentStatusCountRow> rows)
    {
        var scheduled = 0;
        var completed = 0;
        var cancelled = 0;
        var noShow = 0;
        foreach (var row in rows)
        {
            switch (row.Status)
            {
                case AppointmentStatus.Scheduled:
                    scheduled = row.Count;
                    break;
                case AppointmentStatus.Completed:
                    completed = row.Count;
                    break;
                case AppointmentStatus.Cancelled:
                    cancelled = row.Count;
                    break;
                case AppointmentStatus.NoShow:
                    noShow = row.Count;
                    break;
            }
        }

        return new AppointmentReportStatusCountsDto(scheduled, completed, cancelled, noShow);
    }
}
