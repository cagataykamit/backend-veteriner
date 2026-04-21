using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Reports.Vaccinations;

/// <summary>Rapor <c>from</c>/<c>to</c> filtresi ile JSON <c>effectiveReportDateUtc</c> için status bazlı tarih ekseni.</summary>
internal static class VaccinationsReportEffectiveDate
{
    /// <summary>
    /// Applied → <see cref="Vaccination.AppliedAtUtc"/>; Scheduled ve Cancelled → <see cref="Vaccination.DueAtUtc"/> (ürün dilinde “sonraki/planlanan”).
    /// </summary>
    public static DateTime? FromEntity(Vaccination v)
        => v.Status switch
        {
            VaccinationStatus.Applied => v.AppliedAtUtc,
            VaccinationStatus.Scheduled => v.DueAtUtc,
            VaccinationStatus.Cancelled => v.DueAtUtc,
            _ => null,
        };
}
