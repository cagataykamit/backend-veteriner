namespace Backend.Veteriner.Application.Prescriptions;

/// <summary>Reçete eşzamanlılık çakışması (ADR-043 K5); güncelleme ve iptal aynı yanıtı verir.</summary>
internal static class PrescriptionConcurrency
{
    public const string ConflictCode = "Prescriptions.ConcurrencyConflict";
    public const string ConflictMessage = "Reçete eşzamanlı olarak güncellendi; formu yenileyip tekrar deneyin.";
}
