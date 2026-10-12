namespace Backend.Veteriner.Domain.Products;

/// <summary>Eklenebilir stok defteri kaydı (append-only).</summary>
public sealed class StockMovement
{
    /// <summary>Tek hareketteki miktar üst sınırı (anlamsız değer ve SQL taşması koruması).</summary>
    public const decimal MaxQuantity = 1_000_000m;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid ProductId { get; private set; }

    public StockMovementType MovementType { get; private set; }
    /// <summary>Her zaman pozitif miktar.</summary>
    public decimal Quantity { get; private set; }
    public decimal? UnitCost { get; private set; }
    public string? Reason { get; private set; }
    /// <summary>İleride ödeme/satış bağlantıları için discriminator (nullable).</summary>
    public string? ReferenceType { get; private set; }
    public Guid? ReferenceId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public string? Notes { get; private set; }
    /// <summary>Aynı istemci isteğinin tekrarını tanır (kiracı içinde benzersiz).</summary>
    public Guid? RequestId { get; private set; }
    /// <summary>Yalnız <see cref="StockMovementType.Reversal"/>: geri alınan hareket.</summary>
    public Guid? ReversesMovementId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public Product? Product { get; private set; }

    private StockMovement() { }

    public StockMovement(
        Guid tenantId,
        Guid clinicId,
        Guid productId,
        StockMovementType movementType,
        decimal quantity,
        DateTime occurredAtUtc,
        decimal? unitCost = null,
        string? reason = null,
        string? referenceType = null,
        Guid? referenceId = null,
        Guid? createdByUserId = null,
        string? notes = null,
        Guid? requestId = null,
        Guid? reversesMovementId = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (productId == Guid.Empty)
            throw new ArgumentException("ProductId geçersiz.", nameof(productId));
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Hareket miktarı sıfırdan büyük olmalıdır.");

        TenantId = tenantId;
        ClinicId = clinicId;
        ProductId = productId;
        MovementType = movementType;
        Quantity = quantity;
        UnitCost = unitCost is < 0 ? throw new ArgumentOutOfRangeException(nameof(unitCost)) : unitCost;
        Reason = NormalizeOptional(reason);
        ReferenceType = NormalizeOptional(referenceType);
        ReferenceId = referenceId == Guid.Empty ? null : referenceId;
        OccurredAtUtc = occurredAtUtc;
        CreatedByUserId = createdByUserId == Guid.Empty ? null : createdByUserId;
        Notes = NormalizeOptional(notes);
        RequestId = requestId == Guid.Empty ? null : requestId;
        ReversesMovementId = reversesMovementId == Guid.Empty ? null : reversesMovementId;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Geri alınabilir: yalnız yönü bilinen hareketler (Adjustment hedef sayımdır, Reversal tekrar geri alınmaz).</summary>
    public bool IsReversible => MovementType is StockMovementType.Initial or StockMovementType.In or StockMovementType.Out;

    /// <summary>Geri alma stoğu artırır mı (Out geri alınır) yoksa azaltır mı (Initial/In geri alınır).</summary>
    public bool ReversalIncreasesStock => MovementType == StockMovementType.Out;

    /// <summary>Bu hareketin ters hareketini üretir; kaynak bilgisi izlenebilirlik için kopyalanır.</summary>
    public StockMovement CreateReversal(
        string reason,
        string? notes,
        DateTime occurredAtUtc,
        Guid? createdByUserId,
        Guid? requestId)
    {
        if (!IsReversible)
            throw new InvalidOperationException("Bu hareket tipi geri alınamaz.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Geri alma gerekçesi zorunludur.", nameof(reason));

        return new StockMovement(
            TenantId,
            ClinicId,
            ProductId,
            StockMovementType.Reversal,
            Quantity,
            occurredAtUtc,
            UnitCost,
            reason,
            ReferenceType,
            ReferenceId,
            createdByUserId,
            notes,
            requestId,
            reversesMovementId: Id);
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
