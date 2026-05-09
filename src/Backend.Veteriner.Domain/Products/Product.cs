using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Products;

/// <summary>Kiracı katalog kartı (tenant-wide); stok miktarı <see cref="ProductStock"/> üzerinden klinik bazlı tutulur.</summary>
public sealed class Product : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }

    /// <summary>Ürün grubu (opsiyonel).</summary>
    public Guid? ProductCategoryId { get; private set; }

    public string Name { get; private set; } = default!;
    public string? Sku { get; private set; }
    public string? Barcode { get; private set; }
    public string? Description { get; private set; }

    /// <summary>Birim açıklaması (örn. Adet, Kutu).</summary>
    public string Unit { get; private set; } = default!;

    public decimal UnitPrice { get; private set; }

    /// <summary>ISO 4217 alpha-3.</summary>
    public string Currency { get; private set; } = default!;

    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public ProductCategory? Category { get; private set; }
    public ICollection<ProductStock> Stocks { get; private set; } = new List<ProductStock>();
    public ICollection<StockMovement> Movements { get; private set; } = new List<StockMovement>();

    private Product() { }

    public Product(
        Guid tenantId,
        string name,
        string unit,
        decimal unitPrice,
        string currency,
        Guid? productCategoryId = null,
        string? sku = null,
        string? barcode = null,
        string? description = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Ürün adı boş olamaz.", nameof(name));
        if (string.IsNullOrWhiteSpace(unit))
            throw new ArgumentException("Birim boş olamaz.", nameof(unit));
        if (unitPrice < 0)
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Birim fiyat negatif olamaz.");
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Para birimi boş olamaz.", nameof(currency));

        TenantId = tenantId;
        ProductCategoryId = productCategoryId == Guid.Empty ? null : productCategoryId;
        Name = name.Trim();
        Unit = unit.Trim();
        UnitPrice = unitPrice;
        Currency = currency.Trim().ToUpperInvariant();
        Sku = NormalizeOptional(sku);
        Barcode = NormalizeOptional(barcode);
        Description = NormalizeOptional(description);
        IsActive = true;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = null;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
