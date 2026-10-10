using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Charges;

/// <summary>
/// Müşteriye yazılan ücret (borç) satırı. Tahsilattan (Payment) ayrıdır; hiçbir şeyi ödenmiş saymaz.
/// Müşteri zorunlu, Visit ve Muayene opsiyoneldir. Tutar bu aggregate'te hesaplanır; para birimi şimdilik yalnızca TRY.
/// </summary>
public sealed class Charge : AggregateRoot
{
    public const string DefaultCurrency = "TRY";
    public const int MaxDescriptionLength = 500;
    public const decimal MaxQuantity = 100_000m;
    public const decimal MaxUnitPrice = 10_000_000m;
    public const int QuantityDecimals = 3;
    public const int UnitPriceDecimals = 2;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid? PetId { get; private set; }
    public Guid? VisitId { get; private set; }
    public Guid? ExaminationId { get; private set; }
    public string Description { get; private set; } = default!;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }

    /// <summary><c>round(Quantity × UnitPrice, 2)</c>; tek hesap yeri <see cref="Create"/>.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = DefaultCurrency;

    /// <summary>Ödemelerden tahsis edilen toplam; <c>0 ≤ AllocatedAmount ≤ Amount</c>. Yalnızca <see cref="ApplyAllocation"/> değiştirir.</summary>
    public decimal AllocatedAmount { get; private set; }

    public decimal OutstandingAmount => Amount - AllocatedAmount;

    /// <summary>Başarılı mutasyon sayacı; optimistic concurrency token. Her tahsis 1 artırır.</summary>
    public long MutationSequence { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>İstemci tekrar koruması anahtarı; aynı kiracıda benzersizdir, verilmeyebilir.</summary>
    public Guid? RequestId { get; private set; }

    private Charge() { }

    public static Result<Charge> Create(
        Guid tenantId,
        Guid clinicId,
        Guid clientId,
        Guid? petId,
        Guid? visitId,
        Guid? examinationId,
        string? description,
        decimal quantity,
        decimal unitPrice,
        Guid createdByUserId,
        DateTime createdAtUtc,
        Guid? requestId)
    {
        if (tenantId == Guid.Empty)
            return Invalid("TenantId geçersiz.");
        if (clinicId == Guid.Empty)
            return Invalid("ClinicId geçersiz.");
        if (clientId == Guid.Empty)
            return Invalid("ClientId zorunludur.");
        if (petId == Guid.Empty || visitId == Guid.Empty || examinationId == Guid.Empty || requestId == Guid.Empty)
            return Invalid("PetId, VisitId, ExaminationId ve RequestId verilirse geçerli bir kimlik olmalıdır.");
        if (createdByUserId == Guid.Empty)
            return Invalid("Oluşturan kullanıcı geçersiz.");

        var text = description?.Trim();
        if (string.IsNullOrEmpty(text))
            return Invalid("Açıklama zorunludur.");
        if (text.Length > MaxDescriptionLength)
            return Invalid($"Açıklama en fazla {MaxDescriptionLength} karakter olabilir.");

        if (quantity <= 0 || quantity > MaxQuantity || decimal.Round(quantity, QuantityDecimals) != quantity)
            return Invalid($"Miktar 0'dan büyük, en fazla {MaxQuantity:0} ve en fazla {QuantityDecimals} ondalıklı olmalıdır.");
        if (unitPrice <= 0 || unitPrice > MaxUnitPrice || decimal.Round(unitPrice, UnitPriceDecimals) != unitPrice)
            return Invalid($"Birim fiyat 0'dan büyük, en fazla {MaxUnitPrice:0} ve en fazla {UnitPriceDecimals} ondalıklı olmalıdır.");

        var amount = decimal.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0)
            return Invalid("Tutar sıfıra yuvarlanamaz; miktar ve birim fiyatı kontrol edin.");

        return Result<Charge>.Success(new Charge
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            ClientId = clientId,
            PetId = petId,
            VisitId = visitId,
            ExaminationId = examinationId,
            Description = text,
            Quantity = quantity,
            UnitPrice = unitPrice,
            Amount = amount,
            Currency = DefaultCurrency,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = NormalizeUtc(createdAtUtc),
            RequestId = requestId,
        });
    }

    /// <summary>Tahsis sayacını artırır; açık tutarı aşamaz.</summary>
    public Result ApplyAllocation(decimal amount)
    {
        if (amount <= 0)
            return Result.Failure("Charges.Validation", "Tahsis tutarı 0'dan büyük olmalıdır.");

        if (amount > OutstandingAmount)
        {
            return Result.Failure(
                "PaymentAllocations.ExceedsOutstanding",
                $"Tahsis tutarı ücretin açık tutarını aşıyor (ChargeId={Id}, açık tutar={OutstandingAmount:0.00}).");
        }

        AllocatedAmount += amount;
        MutationSequence++;
        return Result.Success();
    }

    private static Result<Charge> Invalid(string message)
        => Result<Charge>.Failure("Charges.Validation", message);

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
