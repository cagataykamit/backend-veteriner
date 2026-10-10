namespace Backend.Veteriner.Domain.Pets;

/// <summary>
/// Hayvanın kalıcı klinik güvenlik uyarıları (bayrak kümesi). Triage skoru değildir; ziyaret içeriği değildir.
/// Yeni bayrak eklemek yalnızca yeni bir bit değeridir (veritabanında int).
/// </summary>
[Flags]
public enum PetAlertFlags
{
    None = 0,
    Allergy = 1,
    Aggressive = 2,
    ChronicCondition = 4,
    AnesthesiaRisk = 8
}

/// <summary>Bayrak kümesi ile API adları arasındaki tek eşleme (sıra sabit).</summary>
public static class PetAlertFlagNames
{
    private static readonly (PetAlertFlags Flag, string Name)[] Known =
    [
        (PetAlertFlags.Allergy, nameof(PetAlertFlags.Allergy)),
        (PetAlertFlags.Aggressive, nameof(PetAlertFlags.Aggressive)),
        (PetAlertFlags.ChronicCondition, nameof(PetAlertFlags.ChronicCondition)),
        (PetAlertFlags.AnesthesiaRisk, nameof(PetAlertFlags.AnesthesiaRisk)),
    ];

    /// <summary>Kümedeki bayrak adları, sabit sırada; boş küme için boş liste.</summary>
    public static IReadOnlyList<string> ToNames(PetAlertFlags flags)
        => Known.Where(k => flags.HasFlag(k.Flag)).Select(k => k.Name).ToList();

    /// <summary>Ad listesini kümeye çevirir (büyük/küçük harf duyarsız, tekrar yok sayılır); bilinmeyen ad başarısızdır.</summary>
    public static bool TryParse(IEnumerable<string> names, out PetAlertFlags flags)
    {
        flags = PetAlertFlags.None;
        foreach (var raw in names)
        {
            var match = Known.FirstOrDefault(k => string.Equals(k.Name, raw?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match.Name is null)
                return false;

            flags |= match.Flag;
        }

        return true;
    }
}
