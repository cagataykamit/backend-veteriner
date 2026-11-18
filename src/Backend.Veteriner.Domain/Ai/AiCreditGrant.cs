using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Ai;

/// <summary>
/// Kliniğe eklenen AI fonu (ADR-044 madde 6): aylık kotadan sonra harcanır ve ay sonunda devreder. Fon bakiyesi bu kayıtların
/// toplamından, kullanım kayıtlarının fondan karşılanan kısmı (<see cref="AiUsageRecord.FundCredits"/>) düşülerek hesaplanır;
/// değişen bir bakiye alanı tutulmaz (eşzamanlı isteklerde kayıp yazma olmaz). Yalnız eklenir, değişmez. Satın alma ücretli
/// ödeme açılınca eklenir; şimdilik yalnız platform yöneticisi elle ekler.
/// </summary>
public sealed class AiCreditGrant : ITenantOwned
{
    public const int NoteMaxLength = 200;

    /// <summary>Tek seferde eklenebilecek en çok kredi (yanlış yazımı sınırlamak için).</summary>
    public const int MaxCredits = 100_000;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public AiCreditGrantKind Kind { get; private set; }
    public int Credits { get; private set; }
    public string? Note { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private AiCreditGrant() { }

    public static AiCreditGrant CreateManual(Guid tenantId, Guid clinicId, int credits, string? note, Guid? userId, DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (credits is <= 0 or > MaxCredits)
            throw new ArgumentOutOfRangeException(nameof(credits), $"Kredi 1 ile {MaxCredits} arasında olmalıdır.");

        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        return new AiCreditGrant
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            Kind = AiCreditGrantKind.Manual,
            Credits = credits,
            Note = trimmed is { Length: > NoteMaxLength } ? trimmed[..NoteMaxLength] : trimmed,
            CreatedByUserId = userId,
            CreatedAtUtc = utcNow,
        };
    }
}
