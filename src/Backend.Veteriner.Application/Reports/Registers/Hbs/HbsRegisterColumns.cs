using Backend.Veteriner.Application.Hbs;

namespace Backend.Veteriner.Application.Reports.Registers.Hbs;

/// <summary>HBS takibi — reçeteler: sütunların tek tanımı (CSV ve XLSX bunu okur).</summary>
internal static class HbsPrescriptionRegisterColumns
{
    public static readonly IReadOnlyList<RegisterColumn<HbsPrescriptionRegisterItemDto>> All =
    [
        RegisterColumn<HbsPrescriptionRegisterItemDto>.Number(null, "Sıra No", r => r.RowNumber),
        RegisterColumn<HbsPrescriptionRegisterItemDto>.Date(null, "Reçete Tarihi", r => r.PrescribedAtUtc),
        RegisterColumn<HbsPrescriptionRegisterItemDto>.Text(null, "Hasta Sahibi", r => r.OwnerName),
        RegisterColumn<HbsPrescriptionRegisterItemDto>.Text(null, "Telefon", r => r.OwnerPhone),
        RegisterColumn<HbsPrescriptionRegisterItemDto>.Text(null, "Hasta", r => r.PetName),
        RegisterColumn<HbsPrescriptionRegisterItemDto>.Text(null, "Tür", r => r.Species),
        RegisterColumn<HbsPrescriptionRegisterItemDto>.Text(null, "Çip No", r => r.MicrochipNumber),
        RegisterColumn<HbsPrescriptionRegisterItemDto>.Text(null, "HBS Hayvan Kimlik No", r => r.HbsAnimalId),
        RegisterColumn<HbsPrescriptionRegisterItemDto>.Text(null, "Reçete", r => r.Title),
        RegisterColumn<HbsPrescriptionRegisterItemDto>.Text(null, "İlaçlar", r => r.Medications),
        RegisterColumn<HbsPrescriptionRegisterItemDto>.Text(null, "Veteriner Hekim", r => r.VeterinarianName),
        RegisterColumn<HbsPrescriptionRegisterItemDto>.Text(null, "HBS Durumu", r => HbsStatusLabels.ToText(r.HbsStatus)),
        RegisterColumn<HbsPrescriptionRegisterItemDto>.Text(null, "e-Reçete No", r => r.ReferenceNumber),
        RegisterColumn<HbsPrescriptionRegisterItemDto>.Date(null, "HBS İşaret Tarihi", r => r.MarkedAtUtc),
    ];

    public static readonly RegisterExportSpec<HbsPrescriptionRegisterItemDto> Export = new(All, "HBS Reçeteler", "hbs-receteler", "HBS Takibi — Reçeteler");
}

/// <summary>HBS takibi — aşılar: sütunların tek tanımı (CSV ve XLSX bunu okur).</summary>
internal static class HbsVaccinationRegisterColumns
{
    public static readonly IReadOnlyList<RegisterColumn<HbsVaccinationRegisterItemDto>> All =
    [
        RegisterColumn<HbsVaccinationRegisterItemDto>.Number(null, "Sıra No", r => r.RowNumber),
        RegisterColumn<HbsVaccinationRegisterItemDto>.Date(null, "Uygulama Tarihi", r => r.AppliedAtUtc),
        RegisterColumn<HbsVaccinationRegisterItemDto>.Text(null, "Hasta Sahibi", r => r.OwnerName),
        RegisterColumn<HbsVaccinationRegisterItemDto>.Text(null, "Telefon", r => r.OwnerPhone),
        RegisterColumn<HbsVaccinationRegisterItemDto>.Text(null, "Hasta", r => r.PetName),
        RegisterColumn<HbsVaccinationRegisterItemDto>.Text(null, "Tür", r => r.Species),
        RegisterColumn<HbsVaccinationRegisterItemDto>.Text(null, "Çip No", r => r.MicrochipNumber),
        RegisterColumn<HbsVaccinationRegisterItemDto>.Text(null, "HBS Hayvan Kimlik No", r => r.HbsAnimalId),
        RegisterColumn<HbsVaccinationRegisterItemDto>.Text(null, "Aşı", r => r.VaccineName),
        RegisterColumn<HbsVaccinationRegisterItemDto>.Text(null, "Seri No", r => r.LotNumber),
        RegisterColumn<HbsVaccinationRegisterItemDto>.CalendarDate(null, "S.K.T", r => r.ExpiryDate),
        RegisterColumn<HbsVaccinationRegisterItemDto>.Text(null, "Veteriner Hekim", r => r.VeterinarianName),
        RegisterColumn<HbsVaccinationRegisterItemDto>.Text(null, "HBS Durumu", r => HbsStatusLabels.ToText(r.HbsStatus)),
        RegisterColumn<HbsVaccinationRegisterItemDto>.Text(null, "ATS Belge Seri No", r => r.ReferenceNumber),
        RegisterColumn<HbsVaccinationRegisterItemDto>.Date(null, "HBS İşaret Tarihi", r => r.MarkedAtUtc),
    ];

    public static readonly RegisterExportSpec<HbsVaccinationRegisterItemDto> Export = new(All, "HBS Aşılar", "hbs-asilar", "HBS Takibi — Aşılar");
}
