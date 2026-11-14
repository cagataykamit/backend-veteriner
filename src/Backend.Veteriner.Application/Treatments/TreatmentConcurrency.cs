namespace Backend.Veteriner.Application.Treatments;

/// <summary>Tedavi eşzamanlılık çakışması (ADR-043 K5); güncelleme ve iptal aynı yanıtı verir.</summary>
internal static class TreatmentConcurrency
{
    public const string ConflictCode = "Treatments.ConcurrencyConflict";
    public const string ConflictMessage = "Tedavi kaydı eşzamanlı olarak güncellendi; formu yenileyip tekrar deneyin.";
}
