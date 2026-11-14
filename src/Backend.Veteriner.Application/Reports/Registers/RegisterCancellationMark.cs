namespace Backend.Veteriner.Application.Reports.Registers;

/// <summary>
/// Kayıt defterinde iptal edilen kaydın işareti (tek yer): kayıt defterden çıkarılmaz, metninin sonuna ` (iptal)` eklenir
/// (RECORD-008, ADR-043 K4).
/// </summary>
public static class RegisterCancellationMark
{
    public const string Suffix = " (iptal)";

    public static string Apply(string text, bool isCancelled) => isCancelled ? text + Suffix : text;
}
