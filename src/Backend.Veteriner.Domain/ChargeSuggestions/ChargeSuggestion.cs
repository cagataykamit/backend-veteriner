using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.ChargeSuggestions;

/// <summary>
/// "Ücret bekliyor" kalemi (ADR-018): hekimin ücretlendirilebilir işleminden (stoktan düşen uygulama veya muayenede yapılan hizmet) aynı transaction'da doğar,
/// ücret kaydı değildir. Resepsiyon ücrete çevirir veya gerekçeyle ücretsiz sayar; kaynak işlem iptal edilirse kalem düşer.
/// Durum yalnız <see cref="ChargeSuggestionStatus.Pending"/>'den bir kez değişir ve geri dönmez (ücret iptali kalemi yeniden açmaz).
/// Eşzamanlı kararları <see cref="RowVersion"/> ayırır: tek kazanan olur.
/// </summary>
public sealed class ChargeSuggestion : AggregateRoot, IClinicScoped
{
    /// <summary>Kaynak türü: stoktan düşen uygulama (aşı kaydıyla yazılanlar dahil).</summary>
    public const string AdministrationSource = "Administration";

    /// <summary>
    /// Kaynak türü: muayenede yapılan hizmet (stoksuz katalog kalemi; bugün yalnız muayene şablonu uygulamasından, ADR-020 S4).
    /// Ayrı bir işlem kaydı yoktur; kalemin kendisi hizmet kaydıdır (<see cref="SourceId"/> = <see cref="Id"/>).
    /// </summary>
    public const string ExaminationServiceSource = "ExaminationService";

    public const int MaxSourceTypeLength = 50;
    public const int MaxUnitLength = 50;

    public const string AlreadyConvertedCode = "ChargeSuggestions.AlreadyConverted";
    public const string AlreadyWaivedCode = "ChargeSuggestions.AlreadyWaived";
    public const string SourceVoidedCode = "ChargeSuggestions.SourceAlreadyVoided";

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid PetId { get; private set; }
    public Guid? ExaminationId { get; private set; }

    /// <summary>Muayenenin kalem doğduğu andaki geliş kaydı (Bugün süzgeci); ücrete çevirmede muayenenin güncel geliş bağı kullanılır.</summary>
    public Guid? VisitId { get; private set; }

    public string SourceType { get; private set; } = default!;
    public Guid SourceId { get; private set; }
    public Guid ProductId { get; private set; }

    /// <summary>Ücret açıklaması önerisi (ürün/hizmet adı, kalem doğduğu andaki).</summary>
    public string Description { get; private set; } = default!;

    public decimal Quantity { get; private set; }
    public string Unit { get; private set; } = default!;

    /// <summary>Kalem doğduğu andaki katalog birim fiyatı; fiyat yoksa, TRY değilse veya ücret kuralına uymuyorsa <c>null</c> (resepsiyon girer).</summary>
    public decimal? SuggestedUnitPrice { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public ChargeSuggestionStatus Status { get; private set; }

    /// <summary>Ücrete çevrildiyse oluşan ücret.</summary>
    public Guid? ChargeId { get; private set; }

    /// <summary>Durumu Pending'den çıkaran karar (çevirme, ücretsiz sayma veya kaynak iptali) zamanı ve kullanıcısı.</summary>
    public DateTime? ResolvedAtUtc { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }

    /// <summary>Yalnız <see cref="ChargeSuggestionStatus.Waived"/> iken dolu zorunlu gerekçe.</summary>
    public string? WaiveReason { get; private set; }

    public byte[] RowVersion { get; private set; } = default!;

    public bool IsPending => Status == ChargeSuggestionStatus.Pending;

    private ChargeSuggestion() { }

    /// <summary>Katalogda bedelsiz (fiyat 0) tanımlı ürün ücretlendirilmez, kalem doğurmaz (pilot kuralı, ADR-018).</summary>
    public static bool IsChargeable(Product product) => product.UnitPrice > 0;

    /// <summary>Uygulamadan kalem üretir; fiyat ürün kataloğundan (kalem doğduğu andaki) alınır.</summary>
    public static Result<ChargeSuggestion> ForAdministration(Administration administration, Product product, Guid? visitId)
    {
        if (administration.ProductId != product.Id || administration.TenantId != product.TenantId)
            return Result<ChargeSuggestion>.Failure("ChargeSuggestions.Validation", "Uygulama ile ürün uyuşmuyor.");

        return Result<ChargeSuggestion>.Success(new ChargeSuggestion
        {
            TenantId = administration.TenantId,
            ClinicId = administration.ClinicId,
            PetId = administration.PetId,
            ExaminationId = administration.ExaminationId,
            VisitId = visitId == Guid.Empty ? null : visitId,
            SourceType = AdministrationSource,
            SourceId = administration.Id,
            ProductId = product.Id,
            Description = Truncate(product.Name, Charge.MaxDescriptionLength),
            Quantity = administration.Quantity,
            Unit = Truncate(product.Unit, MaxUnitLength),
            SuggestedUnitPrice = CatalogPrice(product),
            OccurredAtUtc = administration.AdministeredAtUtc,
            CreatedAtUtc = administration.CreatedAtUtc,
            Status = ChargeSuggestionStatus.Pending,
        });
    }

    /// <summary>
    /// Muayenede yapılan hizmetten kalem üretir (stok yok); fiyat ürün kataloğundan (kalem doğduğu andaki) alınır.
    /// Bedelsiz hizmet için çağıran <see cref="IsChargeable"/> ile kalem üretmez (uygulamayla aynı kural).
    /// </summary>
    public static Result<ChargeSuggestion> ForExaminationService(
        Examination examination, Product product, decimal quantity, DateTime nowUtc)
    {
        if (product.Kind != ProductKind.Service || examination.TenantId != product.TenantId)
            return Result<ChargeSuggestion>.Failure("ChargeSuggestions.Validation", "Hizmet kalemi yalnız kiracının hizmet ürünüyle oluşur.");

        if (quantity <= 0 || quantity > Charge.MaxQuantity || decimal.Round(quantity, Charge.QuantityDecimals) != quantity)
            return Result<ChargeSuggestion>.Failure("ChargeSuggestions.Validation", "Hizmet miktarı geçersiz.");

        var occurredAtUtc = NormalizeUtc(nowUtc);
        var suggestion = new ChargeSuggestion
        {
            TenantId = examination.TenantId,
            ClinicId = examination.ClinicId,
            PetId = examination.PetId,
            ExaminationId = examination.Id,
            VisitId = examination.VisitId,
            SourceType = ExaminationServiceSource,
            ProductId = product.Id,
            Description = Truncate(product.Name, Charge.MaxDescriptionLength),
            Quantity = quantity,
            Unit = Truncate(product.Unit, MaxUnitLength),
            SuggestedUnitPrice = CatalogPrice(product),
            OccurredAtUtc = occurredAtUtc,
            CreatedAtUtc = occurredAtUtc,
            Status = ChargeSuggestionStatus.Pending,
        };
        suggestion.SourceId = suggestion.Id;
        return Result<ChargeSuggestion>.Success(suggestion);
    }

    /// <summary>Kalem hâlâ karar bekliyor mu; değilse son duruma göre tek hata kodu.</summary>
    public Result EnsurePending()
        => Status switch
        {
            ChargeSuggestionStatus.Pending => Result.Success(),
            ChargeSuggestionStatus.Converted => Result.Failure(AlreadyConvertedCode, "Bu kalem zaten ücrete çevrilmiş."),
            ChargeSuggestionStatus.Waived => Result.Failure(AlreadyWaivedCode, "Bu kalem ücretsiz sayılmış."),
            _ => Result.Failure(SourceVoidedCode, "Kalemin kaynağı olan işlem iptal edilmiş."),
        };

    /// <summary>Kalemi oluşan ücrete bağlar.</summary>
    public Result MarkConverted(Guid chargeId, Guid userId, DateTime nowUtc)
    {
        var pending = EnsurePending();
        if (!pending.IsSuccess)
            return pending;

        if (chargeId == Guid.Empty || userId == Guid.Empty)
            return Result.Failure("ChargeSuggestions.Validation", "Ücret ve kullanıcı kimliği geçerli olmalıdır.");

        Status = ChargeSuggestionStatus.Converted;
        ChargeId = chargeId;
        Resolve(userId, nowUtc);
        return Result.Success();
    }

    /// <summary>Kalemi gerekçeyle ücretsiz sayar; geri alınmaz.</summary>
    public Result Waive(Guid userId, string? reason, DateTime nowUtc)
    {
        var pending = EnsurePending();
        if (!pending.IsSuccess)
            return pending;

        if (userId == Guid.Empty)
            return Result.Failure("ChargeSuggestions.Validation", "Kullanıcı geçersiz.");

        var reasonCheck = CorrectionReason.Validate(reason, "ChargeSuggestions.Validation");
        if (!reasonCheck.IsSuccess)
            return reasonCheck;

        Status = ChargeSuggestionStatus.Waived;
        WaiveReason = reason!.Trim();
        Resolve(userId, nowUtc);
        return Result.Success();
    }

    /// <summary>Kaynak işlem iptal edildi: bekleyen kalem düşer. Zaten karara bağlanmış kalem (ücret kesilmiş/ücretsiz) değişmez.</summary>
    public bool CancelForVoidedSource(Guid userId, DateTime nowUtc)
    {
        if (!IsPending)
            return false;

        Status = ChargeSuggestionStatus.Cancelled;
        Resolve(userId, nowUtc);
        return true;
    }

    private void Resolve(Guid userId, DateTime nowUtc)
    {
        ResolvedAtUtc = NormalizeUtc(nowUtc);
        ResolvedByUserId = userId;
    }

    private static decimal? CatalogPrice(Product product)
    {
        var price = product.UnitPrice;
        var usable = price > 0
            && price <= Charge.MaxUnitPrice
            && decimal.Round(price, Charge.UnitPriceDecimals) == price
            && string.Equals(product.Currency, Charge.DefaultCurrency, StringComparison.OrdinalIgnoreCase);
        return usable ? price : null;
    }

    private static string Truncate(string value, int maxLength)
    {
        var text = value.Trim();
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
