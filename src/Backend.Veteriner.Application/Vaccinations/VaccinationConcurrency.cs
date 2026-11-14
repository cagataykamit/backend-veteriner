namespace Backend.Veteriner.Application.Vaccinations;

/// <summary>Aşı kaydı eşzamanlılık çakışması (ADR-043 K5); güncelleme ve iptal aynı yanıtı verir.</summary>
internal static class VaccinationConcurrency
{
    public const string ConflictCode = "Vaccinations.ConcurrencyConflict";
    public const string ConflictMessage = "Aşı kaydı eşzamanlı olarak güncellendi; formu yenileyip tekrar deneyin.";
}
