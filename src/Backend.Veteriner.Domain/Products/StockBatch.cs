using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Products;

/// <summary>
/// Bir klinikteki bir ürünün tek stok girişi (parti/lot). Her giriş (<c>In</c>/<c>Initial</c>) yeni parti açar;
/// kalan miktar yalnız stok hareketleriyle değişir. Parti bilgisi olmayan (eski) bakiye parti sayılmaz:
/// <c>ProductStock.QuantityOnHand − Σ RemainingQuantity</c> olarak türetilir.
/// </summary>
public sealed class StockBatch : ITenantOwned
{
    public const int MaxLotNumberLength = 100;
    public const int MaxSupplierNameLength = 200;
    public const int MaxInvoiceNumberLength = 100;
    public const int MaxTransportLength = 200;
    public const string LotAndExpiryRequiredCode = "StockBatches.LotAndExpiryRequired";
    public const string ValidationCode = "StockBatches.Validation";
    public const string ExpiredAcknowledgementRequiredCode = "StockBatches.ExpiredAcknowledgementRequired";

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid ProductId { get; private set; }

    /// <summary>Seri/lot numarası.</summary>
    public string? LotNumber { get; private set; }

    /// <summary>Son kullanma tarihi (bu gün dahil kullanılabilir).</summary>
    public DateOnly? ExpiryDate { get; private set; }

    public decimal InitialQuantity { get; private set; }
    public decimal RemainingQuantity { get; private set; }

    /// <summary>Satın alınan firma (serbest metin; cari modülü yok).</summary>
    public string? SupplierName { get; private set; }
    public string? InvoiceNumber { get; private set; }
    public DateOnly? InvoiceDate { get; private set; }

    /// <summary>Geliş/teslim tarihi.</summary>
    public DateOnly? ReceivedDate { get; private set; }

    /// <summary>Tedarikçi depo çıkış tarihi.</summary>
    public DateOnly? DispatchedDate { get; private set; }

    /// <summary>Nakil şekli (soğuk zincir).</summary>
    public string? TransportMethod { get; private set; }

    /// <summary>Nakilde kullanılan izleme cihazı / soğuk zincir indikatörü.</summary>
    public string? ColdChainMonitor { get; private set; }

    public Guid? CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Eşzamanlı düşümleri ayırt eden belirteç.</summary>
    public byte[] RowVersion { get; private set; } = default!;

    public Product? Product { get; private set; }

    private StockBatch() { }

    public static Result<StockBatch> Create(
        Guid tenantId,
        Guid clinicId,
        Product product,
        decimal quantity,
        StockBatchDetails details,
        Guid? createdByUserId,
        DateTime createdAtUtc)
    {
        if (tenantId == Guid.Empty || clinicId == Guid.Empty || product.Id == Guid.Empty)
            return Invalid("TenantId, ClinicId ve ProductId geçerli olmalıdır.");
        if (quantity <= 0)
            return Invalid("Parti miktarı sıfırdan büyük olmalıdır.");

        var lotNumber = Normalize(details.LotNumber);
        if (product.RequiresLotAndExpiry && (lotNumber is null || details.ExpiryDate is null))
        {
            return Result<StockBatch>.Failure(
                LotAndExpiryRequiredCode,
                "Aşı ve kontrollü ürünlerde seri/lot no ve son kullanma tarihi zorunludur.");
        }

        if (details.DispatchedDate is { } dispatched && details.ReceivedDate is { } received && dispatched > received)
            return Invalid("Depo çıkış tarihi geliş/teslim tarihinden sonra olamaz.");

        return Result<StockBatch>.Success(new StockBatch
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            ProductId = product.Id,
            LotNumber = lotNumber,
            ExpiryDate = details.ExpiryDate,
            InitialQuantity = quantity,
            RemainingQuantity = quantity,
            SupplierName = Normalize(details.SupplierName),
            InvoiceNumber = Normalize(details.InvoiceNumber),
            InvoiceDate = details.InvoiceDate,
            ReceivedDate = details.ReceivedDate,
            DispatchedDate = details.DispatchedDate,
            TransportMethod = Normalize(details.TransportMethod),
            ColdChainMonitor = Normalize(details.ColdChainMonitor),
            CreatedByUserId = createdByUserId == Guid.Empty ? null : createdByUserId,
            CreatedAtUtc = createdAtUtc,
        });
    }

    /// <summary>SKT, verilen günden önceyse süresi geçmiştir (SKT günü dahil kullanılabilir).</summary>
    public bool IsExpiredOn(DateOnly day) => ExpiryDate is { } expiry && expiry < day;

    public void Decrease(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Azalış miktarı sıfırdan büyük olmalıdır.");
        if (RemainingQuantity < amount)
            throw new InvalidOperationException("Parti kalanı yetersiz.");

        RemainingQuantity -= amount;
    }

    public void Increase(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Artış miktarı sıfırdan büyük olmalıdır.");

        RemainingQuantity += amount;
    }

    /// <summary>Sayım düzeltmesi: partinin kalanını doğrudan ayarlar (<c>&gt;= 0</c>).</summary>
    public void SetRemaining(decimal target)
    {
        if (target < 0)
            throw new ArgumentOutOfRangeException(nameof(target), "Parti kalanı negatif olamaz.");

        RemainingQuantity = target;
    }

    /// <summary>
    /// FEFO sırası: SKT artan (SKT'siz sonda), sonra giriş zamanı. Tek partiden düşüm için <paramref name="quantity"/>'yi
    /// karşılayan ilk parti seçilir; <paramref name="notExpiredOn"/> verilirse o güne göre süresi geçmiş partiler elenir.
    /// </summary>
    public static StockBatch? PickFirstSufficient(IEnumerable<StockBatch> candidates, decimal quantity, DateOnly? notExpiredOn)
        => candidates
            .Where(b => b.RemainingQuantity >= quantity && (notExpiredOn is not { } day || !b.IsExpiredOn(day)))
            .OrderBy(b => b.ExpiryDate is null)
            .ThenBy(b => b.ExpiryDate)
            .ThenBy(b => b.CreatedAtUtc)
            .ThenBy(b => b.Id)
            .FirstOrDefault();

    private static Result<StockBatch> Invalid(string message)
        => Result<StockBatch>.Failure(ValidationCode, message);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
