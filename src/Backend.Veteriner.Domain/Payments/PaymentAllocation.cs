namespace Backend.Veteriner.Domain.Payments;

/// <summary>
/// Bir tahsilatın bir ücretin ne kadarını karşıladığının açık kaydı. Yalnızca <see cref="Payment.AllocateFully"/>
/// üretir (sayaç değişmezleri orada korunur); oluşturulduktan sonra değişmez.
/// </summary>
public sealed class PaymentAllocation
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid PaymentId { get; private set; }
    public Guid ChargeId { get; private set; }
    public decimal Amount { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

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
}
