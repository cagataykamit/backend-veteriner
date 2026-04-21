using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Reports.Vaccinations;

internal static class VaccinationStatusTurkishDisplay
{
    public static string ToLabel(VaccinationStatus status)
        => status switch
        {
            VaccinationStatus.Scheduled => "Planlandı",
            VaccinationStatus.Applied => "Uygulandı",
            VaccinationStatus.Cancelled => "İptal",
            _ => status.ToString(),
        };
}
