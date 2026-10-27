using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Payments;

/// <summary>
/// Bir tahsilatın bir ücretin ne kadarını karşıladığının açık kaydı. Yalnızca <see cref="Payment.AllocateFully"/>
/// üretir (sayaç değişmezleri orada korunur); oluşturulduktan sonra tutarı değişmez. Ödeme iptalinde silinmez,
/// <see cref="ReversedAtUtc"/> ile kapatılır (yalnızca <see cref="Payment.Void"/> yapar); aktif tahsis = geri alınmamış.
/// </summary>
public sealed class PaymentAllocation : ITenantOwned
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid PaymentId { get; private set; }
    public Guid ChargeId { get; private set; }
    public decimal Amount { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ReversedAtUtc { get; private set; }

    public bool IsReversed => ReversedAtUtc.HasValue;

    private PaymentAllocation() { }

    internal static PaymentAllocation Create(
        Guid tenantId,
        Guid clinicId,
        Guid paymentId,
        Guid chargeId,
        decimal amount,
        Guid createdByUserId,
        DateTime createdAtUtc)
        => new()
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            PaymentId = paymentId,
            ChargeId = chargeId,
            Amount = amount,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc),
        };

    internal void Reverse(DateTime nowUtc)
        => ReversedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
}
