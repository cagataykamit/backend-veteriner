using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Reports.Vaccinations.Contracts.Dtos;

namespace Backend.Veteriner.Application.Reports.Vaccinations;

/// <summary>Aşı raporu PDF başlığı ve sütunları (XLSX ile aynı sütunlar ve sıra; tarihler İstanbul günü).</summary>
internal static class VaccinationsPdfColumns
{
    public const string Title = "Aşı Raporu";

    public static readonly IReadOnlyList<RegisterColumn<VaccinationReportItemDto>> All =
    [
        RegisterColumn<VaccinationReportItemDto>.Date(null, "Rapor Tarihi", r => r.EffectiveReportDateUtc),
        RegisterColumn<VaccinationReportItemDto>.Text(null, "Klinik", r => r.ClinicName),
        RegisterColumn<VaccinationReportItemDto>.Text(null, "Müşteri", r => r.ClientName),
        RegisterColumn<VaccinationReportItemDto>.Text(null, "Hayvan", r => r.PetName),
        RegisterColumn<VaccinationReportItemDto>.Text(null, "Aşı", r => r.VaccineName),
        RegisterColumn<VaccinationReportItemDto>.Text(null, "Durum", r => VaccinationStatusTurkishDisplay.ToLabel(r.Status)),
        RegisterColumn<VaccinationReportItemDto>.Date(null, "Uygulama Tarihi", r => r.AppliedAtUtc),
        RegisterColumn<VaccinationReportItemDto>.Date(null, "Sonraki Tarih", r => r.NextDueAtUtc),
        RegisterColumn<VaccinationReportItemDto>.Text(null, "Not", r => r.Notes),
    ];
}
