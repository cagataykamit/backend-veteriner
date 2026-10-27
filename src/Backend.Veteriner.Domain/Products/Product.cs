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

    /// <summary>Ürün sınıfı; varsayılan <see cref="ProductKind.Other"/>.</summary>
    public ProductKind Kind { get; private set; }

    /// <summary>Kontrollü sınıf; varsayılan <see cref="ControlledSubstanceClass.None"/>.</summary>
    public ControlledSubstanceClass ControlledClass { get; private set; }

    /// <summary>Farmakolojik şekil (ör. enjeksiyonluk çözelti).</summary>
    public string? PharmacologicalForm { get; private set; }

    /// <summary>Takdim şekli (ör. flakon).</summary>
    public string? PresentationForm { get; private set; }

    /// <summary>Ambalaj (ör. 10 ml flakon).</summary>
    public string? Packaging { get; private set; }

    /// <summary>Hizmet dışındaki ürünler stok takiplidir; stok düşen/giren her yüzey bu kuralı kullanır.</summary>
    public bool IsStockTracked => Kind != ProductKind.Service;

    /// <summary>Aşı ve kontrollü ürünlerde stok girişinde seri/lot no ve son kullanma tarihi zorunludur.</summary>
    public bool RequiresLotAndExpiry => Kind == ProductKind.Vaccine || ControlledClass != ControlledSubstanceClass.None;

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public ProductCategory? Category { get; private set; }
    public ICollection<ProductStock> Stocks { get; private set; } = new List<ProductStock>();
    public ICollection<StockMovement> Movements { get; private set; } = new List<StockMovement>();
    public ICollection<StockBatch> StockBatches { get; private set; } = new List<StockBatch>();

    private Product() { }

    public Product(
        Guid tenantId,
        string name,
        string unit,
        decimal unitPrice,
        string currency,
        DateTime utcNow,
        Guid? productCategoryId = null,
        string? sku = null,
        string? barcode = null,
        string? description = null,
        ProductKind kind = ProductKind.Other,
        ControlledSubstanceClass controlledClass = ControlledSubstanceClass.None,
        string? pharmacologicalForm = null,
        string? presentationForm = null,
        string? packaging = null)
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

        if (kind == ProductKind.Service && controlledClass != ControlledSubstanceClass.None)
            throw new ArgumentException("Hizmet kontrollü sınıfta olamaz.", nameof(controlledClass));

        TenantId = tenantId;
        ProductCategoryId = productCategoryId == Guid.Empty ? null : productCategoryId;
        Name = name.Trim();
        Unit = unit.Trim();
        UnitPrice = unitPrice;
        Currency = currency.Trim().ToUpperInvariant();
        Sku = NormalizeOptional(sku);
        Barcode = NormalizeOptional(barcode);
        Description = NormalizeOptional(description);
        Kind = kind;
        ControlledClass = controlledClass;
        PharmacologicalForm = NormalizeOptional(pharmacologicalForm);
        PresentationForm = NormalizeOptional(presentationForm);
        Packaging = NormalizeOptional(packaging);
        IsActive = true;
        CreatedAtUtc = utcNow;
        UpdatedAtUtc = null;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void Update(
        Guid? productCategoryId,
        string name,
        string unit,
        decimal unitPrice,
        string currency,
        DateTime utcNow,
        string? sku = null,
        string? barcode = null,
        string? description = null,
        ProductKind kind = ProductKind.Other,
        ControlledSubstanceClass controlledClass = ControlledSubstanceClass.None,
        string? pharmacologicalForm = null,
        string? presentationForm = null,
        string? packaging = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Ürün adı boş olamaz.", nameof(name));
        if (string.IsNullOrWhiteSpace(unit))
            throw new ArgumentException("Birim boş olamaz.", nameof(unit));
        if (unitPrice < 0)
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Birim fiyat negatif olamaz.");
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Para birimi boş olamaz.", nameof(currency));
        if (kind == ProductKind.Service && controlledClass != ControlledSubstanceClass.None)
            throw new ArgumentException("Hizmet kontrollü sınıfta olamaz.", nameof(controlledClass));

        ProductCategoryId = productCategoryId == Guid.Empty ? null : productCategoryId;
        Name = name.Trim();
        Unit = unit.Trim();
        UnitPrice = unitPrice;
        Currency = currency.Trim().ToUpperInvariant();
        Sku = NormalizeOptional(sku);
        Barcode = NormalizeOptional(barcode);
        Description = NormalizeOptional(description);
        Kind = kind;
        ControlledClass = controlledClass;
        PharmacologicalForm = NormalizeOptional(pharmacologicalForm);
        PresentationForm = NormalizeOptional(presentationForm);
        Packaging = NormalizeOptional(packaging);
        UpdatedAtUtc = utcNow;
    }

    public void Activate(DateTime utcNow)
    {
        IsActive = true;
        UpdatedAtUtc = utcNow;
    }

    public void Deactivate(DateTime utcNow)
    {
        IsActive = false;
        UpdatedAtUtc = utcNow;
    }
}
