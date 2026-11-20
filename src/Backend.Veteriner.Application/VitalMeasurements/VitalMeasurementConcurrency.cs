namespace Backend.Veteriner.Application.VitalMeasurements;

/// <summary>Vital ölçümü eşzamanlılık çakışması (ADR-043 K5); güncelleme ve iptal aynı yanıtı verir.</summary>
internal static class VitalMeasurementConcurrency
{
    public const string ConflictCode = "VitalMeasurements.ConcurrencyConflict";
    public const string ConflictMessage = "Vital ölçümü eşzamanlı olarak güncellendi; formu yenileyip tekrar deneyin.";
    public const string RowVersionInvalidMessage = "Kayıt sürümü eksik ya da geçersiz. Sayfayı yenileyip tekrar deneyin.";
}
