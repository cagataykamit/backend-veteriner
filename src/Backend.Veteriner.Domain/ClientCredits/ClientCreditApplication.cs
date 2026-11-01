using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.ClientCredits;

/// <summary>
/// Krediden bir ücrete yapılan mahsup (FIN-005 K4). Yalnız <see cref="ClientCredit.Apply"/> üretir; tutarı değişmez.
/// Geri alınınca silinmez, <see cref="ReversedAtUtc"/> ile kapanır (ödeme tahsisiyle aynı desen); aktif mahsup = geri alınmamış.
/// </summary>
public sealed class ClientCreditApplication : ITenantOwned
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid CreditId { get; private set; }
    public Guid ChargeId { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>İstemci tekrar koruması anahtarı; bir mahsup isteği birden çok satır üretebilir.</summary>
    public Guid? RequestId { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ReversedAtUtc { get; private set; }
    public Guid? ReversedByUserId { get; private set; }
    public string? ReverseReason { get; private set; }

    public bool IsReversed => ReversedAtUtc.HasValue;

    private ClientCreditApplication() { }

    internal static ClientCreditApplication Create(
        Guid tenantId,
        Guid clinicId,
        Guid creditId,
        Guid chargeId,
        decimal amount,
        Guid? requestId,
        Guid createdByUserId,
        DateTime createdAtUtc)
        => new()
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            CreditId = creditId,
            ChargeId = chargeId,
            Amount = amount,
            RequestId = requestId,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc),
        };

    internal void Reverse(Guid userId, string reason, DateTime nowUtc)
    {
        ReversedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        ReversedByUserId = userId;
        ReverseReason = reason;
    }
}
