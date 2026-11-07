namespace Backend.Veteriner.Application.VaccinationSchedules;

/// <summary>
/// Aşı takvimi başlangıç şablonu (TIER-005): yalnız klinik isteyince yüklenir, yüklenince sıradan takvim satırıdır ve düzenlenir.
/// Yaygın kedi/köpek uygulamasından derlenmiş genel bir başlangıçtır; tıbbi tavsiye değildir, hekim gözden geçirir.
/// Tür ve aşı sistem kataloğundaki kodlarla eşlenir (<c>SpeciesAndBreedCatalogSeeder</c>, <c>VaccineDefinitionSeeder</c>).
/// Satırlar ve değerler çalışma varsayımıdır (docs/VACCINATION_SCHEDULE_API_CONTRACT.md §6).
/// </summary>
internal static class VaccinationScheduleStarterTemplate
{
    public sealed record Row(string SpeciesCode, string VaccineCode, int StartAgeWeeks, int DoseCount, int? DoseIntervalWeeks, int? RepeatIntervalMonths);

    public static IReadOnlyList<Row> Rows { get; } =
    [
        new("DOG", "INTERNAL_PARASITE", 4, 1, null, 3),
        new("DOG", "MIXED", 6, 3, 3, 12),
        new("DOG", "CORONA", 6, 2, 3, 12),
        new("DOG", "BRONCHINE", 8, 1, null, 12),
        new("DOG", "EXTERNAL_PARASITE", 8, 1, null, 1),
        new("DOG", "RABIES", 12, 1, null, 12),
        new("CAT", "INTERNAL_PARASITE", 4, 1, null, 3),
        new("CAT", "MIXED", 8, 2, 4, 12),
        new("CAT", "EXTERNAL_PARASITE", 8, 1, null, 1),
        new("CAT", "LEUKEMIA", 9, 2, 4, 12),
        new("CAT", "RABIES", 12, 1, null, 12),
    ];
}
