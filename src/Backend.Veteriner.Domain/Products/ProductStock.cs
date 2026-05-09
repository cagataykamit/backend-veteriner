namespace Backend.Veteriner.Domain.Products;

/// <summary>Klinik bazlı operasyonel stok snapshot (<c>TenantId + ClinicId + ProductId</c> tek satır).</summary>
public sealed class ProductStock
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid ProductId { get; private set; }

    public decimal QuantityOnHand { get; private set; }
    public decimal MinimumStockLevel { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>SQL Server rowversion — eşzamanlı güncelleme için.</summary>
    public byte[] RowVersion { get; private set; } = default!;

    public Product? Product { get; private set; }

    private ProductStock() { }

    public ProductStock(
        Guid tenantId,
        Guid clinicId,
        Guid productId,
        decimal quantityOnHand,
        decimal minimumStockLevel)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (productId == Guid.Empty)
            throw new ArgumentException("ProductId geçersiz.", nameof(productId));
        if (quantityOnHand < 0)
            throw new ArgumentOutOfRangeException(nameof(quantityOnHand), "Miktar negatif olamaz.");
        if (minimumStockLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumStockLevel), "Minimum seviye negatif olamaz.");

        TenantId = tenantId;
        ClinicId = clinicId;
        ProductId = productId;
        QuantityOnHand = quantityOnHand;
        MinimumStockLevel = minimumStockLevel;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void IncreaseQuantity(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Artış miktarı sıfırdan büyük olmalıdır.");

        QuantityOnHand += amount;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void DecreaseQuantity(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Azalış miktarı sıfırdan büyük olmalıdır.");
        if (QuantityOnHand < amount)
            throw new InvalidOperationException("Stok miktarı yetersiz.");

        QuantityOnHand -= amount;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Sayım düzeltmesi: eldeki miktarı doğrudan ayarlar (<c>&gt;= 0</c>).</summary>
    public void SetAbsoluteQuantity(decimal targetQuantityOnHand)
    {
        if (targetQuantityOnHand < 0)
            throw new ArgumentOutOfRangeException(nameof(targetQuantityOnHand), "Stok miktarı negatif olamaz.");

        QuantityOnHand = targetQuantityOnHand;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Minimum stok eşiği (<c>&gt;= 0</c>); eldeki miktarı değiştirmez.</summary>
    public void SetMinimumStockLevel(decimal minimumStockLevel)
    {
        if (minimumStockLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumStockLevel), "Minimum seviye negatif olamaz.");

        MinimumStockLevel = minimumStockLevel;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
