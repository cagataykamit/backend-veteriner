using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Sales;

/// <summary>
/// Muayenesiz ürün satışı (başlık). Satırlar, satış başına tek ücret ve isteğe bağlı ödeme uygulama katmanında
/// mevcut stok/ücret/ödeme komutlarıyla aynı transaction'da yazılır; tutar kuralı yalnızca burada yaşar.
/// Düzenlenmez: yanlış satış gerekçeyle iptal edilir (<see cref="Void"/>).
/// </summary>
public sealed class Sale : AggregateRoot
{
    public const int MaxLines = 50;
    public const int MaxNotesLength = 500;
    public const string Currency = Charge.DefaultCurrency;
    public const string ChargeDescription = "Ürün satışı";

    /// <summary>Satış satırının düştüğü stok hareketinin <c>StockMovement.ReferenceType</c> değeri (kaynak izi).</summary>
    public const string StockReferenceType = "SaleLine";

    private readonly List<SaleLine> _lines = [];

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid ClientId { get; private set; }
    public DateTime SoldAtUtc { get; private set; }

    /// <summary>Satır tutarlarının toplamı; ücret tutarına eşittir.</summary>
    public decimal TotalAmount { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>Satışın tek ücreti; satış kaydı yazıldıktan sonra bağlanır (<see cref="AttachFinance"/>).</summary>
    public Guid? ChargeId { get; private set; }

    /// <summary>Satış anında alınan ödeme; veresiye satışta <c>null</c>.</summary>
    public Guid? PaymentId { get; private set; }

    /// <summary>İptal edilmiş satış silinmez; ödeme/ücret iptal edilmiş, stok iade veya zarar işaretlenmiştir.</summary>
    public DateTime? VoidedAtUtc { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public string? VoidReason { get; private set; }
    public SaleVoidStockOutcome? VoidStockOutcome { get; private set; }
    public bool IsVoided => VoidedAtUtc.HasValue;

    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>İstemci tekrar koruması anahtarı (zorunlu); kiracıda benzersizdir.</summary>
    public Guid RequestId { get; private set; }

    /// <summary>İstek içeriğinin özeti; aynı <see cref="RequestId"/> farklı içerikle gelirse ayırt eder.</summary>
    public string RequestHash { get; private set; } = default!;

    /// <summary>Eşzamanlı iptalleri ayırt eden belirteç.</summary>
    public byte[] RowVersion { get; private set; } = default!;

    public IReadOnlyList<SaleLine> Lines => _lines;

    private Sale() { }

    public static Result<Sale> Create(
        Guid tenantId,
        Guid clinicId,
        Guid clientId,
        DateTime soldAtUtc,
        string? notes,
        IReadOnlyList<SaleLineDraft> lines,
        Guid createdByUserId,
        DateTime createdAtUtc,
        Guid requestId,
        string requestHash)
    {
        if (tenantId == Guid.Empty)
            return Invalid("TenantId geçersiz.");
        if (clinicId == Guid.Empty)
            return Invalid("ClinicId geçersiz.");
        if (clientId == Guid.Empty)
            return Invalid("ClientId geçersiz.");
        if (createdByUserId == Guid.Empty)
            return Invalid("Oluşturan kullanıcı geçersiz.");
        if (requestId == Guid.Empty)
            return Result<Sale>.Failure("Sales.RequestIdRequired", "requestId zorunludur (çift istek koruması).");
        if (string.IsNullOrWhiteSpace(requestHash))
            return Invalid("İstek özeti zorunludur.");

        var text = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (text is not null && text.Length > MaxNotesLength)
            return Invalid($"Not en fazla {MaxNotesLength} karakter olabilir.");

        if (lines.Count == 0)
            return Invalid("Satışta en az bir satır olmalıdır.");
        if (lines.Count > MaxLines)
            return Invalid($"Satışta en fazla {MaxLines} satır olabilir.");

        var sale = new Sale
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            ClientId = clientId,
            SoldAtUtc = NormalizeUtc(soldAtUtc),
            Notes = text,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = NormalizeUtc(createdAtUtc),
            RequestId = requestId,
            RequestHash = requestHash,
        };

        foreach (var draft in lines)
        {
            var line = BuildLine(sale.Id, draft);
            if (!line.IsSuccess)
                return Result<Sale>.Failure(line.Error);

            sale._lines.Add(line.Value!);
        }

        sale.TotalAmount = sale._lines.Sum(l => l.LineAmount);

        // Satışın tek ücreti (Miktar 1 × Toplam); ücret üst sınırını aşan sepet reddedilir.
        if (sale.TotalAmount > Charge.MaxUnitPrice)
            return Invalid($"Satış toplamı en fazla {Charge.MaxUnitPrice:0} olabilir.");

        return Result<Sale>.Success(sale);
    }

    /// <summary>Satış kaydı yazıldıktan sonra üretilen ücreti (ve varsa ödemeyi) bağlar.</summary>
    public void AttachFinance(Guid chargeId, Guid? paymentId)
    {
        if (chargeId == Guid.Empty || paymentId == Guid.Empty)
            throw new ArgumentException("ChargeId ve PaymentId verilirse geçerli bir kimlik olmalıdır.");

        ChargeId = chargeId;
        PaymentId = paymentId;
    }

    /// <summary>
    /// Satışı gerekçe ve stok sonucuyla iptal eder. Ödeme/ücret iptali ve stok iadesi
    /// (<see cref="SaleVoidStockOutcome.StockReturned"/>) çağıranın sorumluluğudur.
    /// </summary>
    public Result Void(Guid voidedByUserId, string? reason, SaleVoidStockOutcome stockOutcome, DateTime nowUtc)
    {
        if (IsVoided)
            return Result.Failure("Sales.AlreadyVoided", "Satış zaten iptal edilmiş.");

        if (voidedByUserId == Guid.Empty)
            return Result.Failure("Sales.Validation", "İptal eden kullanıcı geçersiz.");

        var reasonCheck = CorrectionReason.Validate(reason, "Sales.Validation");
        if (!reasonCheck.IsSuccess)
            return reasonCheck;

        VoidedAtUtc = NormalizeUtc(nowUtc);
        VoidedByUserId = voidedByUserId;
        VoidReason = reason!.Trim();
        VoidStockOutcome = stockOutcome;
        return Result.Success();
    }

    /// <summary>Satır tutarı tek hesap yeri: <c>round(miktar × birim fiyat, 2)</c>; sınırlar ücret satırıyla aynıdır.</summary>
    private static Result<SaleLine> BuildLine(Guid saleId, SaleLineDraft draft)
    {
        if (draft.ProductId == Guid.Empty)
            return Result<SaleLine>.Failure("Sales.Validation", "ProductId zorunludur.");

        if (draft.Quantity <= 0
            || draft.Quantity > Charge.MaxQuantity
            || decimal.Round(draft.Quantity, Charge.QuantityDecimals) != draft.Quantity)
        {
            return Result<SaleLine>.Failure(
                "Sales.Validation",
                $"Miktar 0'dan büyük, en fazla {Charge.MaxQuantity:0} ve en fazla {Charge.QuantityDecimals} ondalıklı olmalıdır.");
        }

        if (draft.UnitPrice <= 0
            || draft.UnitPrice > Charge.MaxUnitPrice
            || decimal.Round(draft.UnitPrice, Charge.UnitPriceDecimals) != draft.UnitPrice)
        {
            return Result<SaleLine>.Failure(
                "Sales.Validation",
                $"Birim fiyat 0'dan büyük, en fazla {Charge.MaxUnitPrice:0} ve en fazla {Charge.UnitPriceDecimals} ondalıklı olmalıdır.");
        }

        var amount = decimal.Round(draft.Quantity * draft.UnitPrice, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0)
            return Result<SaleLine>.Failure("Sales.Validation", "Satır tutarı sıfıra yuvarlanamaz; miktar ve birim fiyatı kontrol edin.");

        return Result<SaleLine>.Success(
            new SaleLine(saleId, draft.ProductId, draft.ProductName, draft.Unit, draft.Quantity, draft.UnitPrice, amount));
    }

    private static Result<Sale> Invalid(string message)
        => Result<Sale>.Failure("Sales.Validation", message);

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}

/// <summary>Satış satırı girdisi: ürün bilgisi çağıran tarafından ürün kaydından kopyalanır.</summary>
public sealed record SaleLineDraft(
    Guid ProductId,
    string ProductName,
    string Unit,
    decimal Quantity,
    decimal UnitPrice);
