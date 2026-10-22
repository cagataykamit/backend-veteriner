namespace Backend.Veteriner.Domain.Hbs;

/// <summary>
/// Kaydın HBS'e (e-Reçete / ATS) girilip girilmediği. Zorunluluk kuralı kodda yoktur (mevzuat doğrulanmadı, ADR-036):
/// gerekmeyen kaydı hekim gerekçeyle <see cref="NotRequired"/> işaretler.
/// </summary>
public enum HbsEntryStatus
{
    NotEntered = 0,
    Entered = 1,
    NotRequired = 2
}
