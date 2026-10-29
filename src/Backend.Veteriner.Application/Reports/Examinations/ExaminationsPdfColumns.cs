using Backend.Veteriner.Application.Reports.Examinations.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.Registers;

namespace Backend.Veteriner.Application.Reports.Examinations;

/// <summary>Muayene raporu PDF başlığı ve sütunları (XLSX ile aynı sütunlar ve sıra; zaman İstanbul).</summary>
internal static class ExaminationsPdfColumns
{
    public const string Title = "Muayene Raporu";

    public static readonly IReadOnlyList<RegisterColumn<ExaminationReportItemDto>> All =
    [
        RegisterColumn<ExaminationReportItemDto>.Time(null, "Muayene Zamanı", r => r.ExaminedAtUtc),
        RegisterColumn<ExaminationReportItemDto>.Text(null, "Klinik", r => r.ClinicName),
        RegisterColumn<ExaminationReportItemDto>.Text(null, "Müşteri", r => r.ClientName),
        RegisterColumn<ExaminationReportItemDto>.Text(null, "Hayvan", r => r.PetName),
        RegisterColumn<ExaminationReportItemDto>.Text(null, "Bağlı Randevu", r => r.AppointmentId.HasValue ? "Var" : "Yok"),
        RegisterColumn<ExaminationReportItemDto>.Text(null, "Geliş Nedeni", r => r.VisitReason),
        RegisterColumn<ExaminationReportItemDto>.Text(null, "Bulgular", r => r.Findings),
        RegisterColumn<ExaminationReportItemDto>.Text(null, "Değerlendirme", r => r.Assessment),
        RegisterColumn<ExaminationReportItemDto>.Text(null, "Not", r => r.Notes),
    ];
}
