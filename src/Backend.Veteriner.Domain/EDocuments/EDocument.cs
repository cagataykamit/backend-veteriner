using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.EDocuments;

/// <summary>
/// Entegratör üzerinden kesilen e-belge (ADR-036 Dilim 3–4). Durum makinesi yalnız burada yaşar:
/// <c>Draft → Sending → Issued | Failed</c>; <c>Failed → Sending (yeniden dene) | Discarded</c>;
/// <c>Issued → CancelRequested → Cancelled | CancelRejected</c>. Kesilmiş belge değiştirilmez.
/// <see cref="ExternalReference"/> bizde üretilir ve gönderimden önce yazılır; yeniden denemede aynı değer gider (çift belge yok).
/// Ücret bağı <c>Charge.EDocumentId</c> sütunundadır; <see cref="HoldsCharges"/> false olunca bağ çözülür.
/// </summary>
public sealed class EDocument : AggregateRoot, IClinicScoped, ITenantOwned
{
    public const int MaxProviderCodeLength = 40;
    public const int MaxProviderIdLength = 100;
    public const int MaxDocumentNumberLength = 100;
    public const int MaxErrorMessageLength = 1000;
    public const int MaxProviderErrorCodeLength = 100;
    public const int MaxRequestHashLength = 64;

    private const string InvalidStateCode = "EDocuments.InvalidState";

    private readonly List<EDocumentLine> _lines = [];

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid? SaleId { get; private set; }
    public Guid? VisitId { get; private set; }

    public EDocumentType Type { get; private set; }
    public EDocumentStatus Status { get; private set; }
    public EDocumentEnvironment Environment { get; private set; }

    /// <summary>Belgeyi kim başlattı: elle, ödeme ekranından veya otomatik kesme ayarıyla.</summary>
    public EDocumentOrigin Origin { get; private set; }

    /// <summary>Sağlayıcı anahtarı (bağlantıdaki <c>ProviderCode</c>); domain sağlayıcı adını bilmez.</summary>
    public string ProviderCode { get; private set; } = default!;

    /// <summary>Sağlayıcıya giden tekrar koruması kimliği (Paraşüt <c>external_reference_key</c>).</summary>
    public Guid ExternalReference { get; private set; }

    /// <summary>İstemci tekrar koruması; kiracıda benzersiz.</summary>
    public Guid RequestId { get; private set; }

    /// <summary>İstek içeriği özeti; aynı <see cref="RequestId"/> farklı içerikle gelirse ayırt eder.</summary>
    public string RequestHash { get; private set; } = default!;

    public string? Note { get; private set; }
    public EDocumentBuyer Buyer { get; private set; } = default!;
    public IReadOnlyList<EDocumentLine> Lines => _lines;

    /// <summary>Satırların KDV dahil toplamı (müşterinin ödediği).</summary>
    public decimal TotalAmount { get; private set; }

    /// <summary>Uygulanan stopaj oranı (%); yalnız stopajlı e-SMM'de dolu.</summary>
    public decimal? WithholdingRate { get; private set; }

    public string? ProviderInvoiceId { get; private set; }
    public string? ProviderDocumentId { get; private set; }
    public string? ProviderJobId { get; private set; }
    public string? DocumentNumber { get; private set; }
    public string? DocumentUuid { get; private set; }

    /// <summary>Sağlayıcının hesapladığı belge toplamı (yuvarlama farkını görmek için).</summary>
    public decimal? ProviderTotal { get; private set; }

    public DateTime? IssuedAtUtc { get; private set; }

    public EDocumentErrorKind? LastErrorKind { get; private set; }
    public string? LastErrorMessage { get; private set; }
    public string? LastProviderErrorCode { get; private set; }

    /// <summary>Gönderim denemesi sayısı (ilk gönderim + yeniden denemeler).</summary>
    public int AttemptCount { get; private set; }

    /// <summary>Arka plan kontrol sayısı (asenkron sonuç / geçici hata); işlemci sınırı bununla uygular.</summary>
    public int CheckCount { get; private set; }

    /// <summary>Arka plan işinin bu belgeye yeniden bakacağı zaman; <c>null</c> = bekleyen iş yok.</summary>
    public DateTime? NextCheckAtUtc { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public string? CancelReason { get; private set; }
    public Guid? CancelRequestedByUserId { get; private set; }
    public DateTime? CancelRequestedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }

    /// <summary>
    /// Ticari e-Faturada alıcı yanıtı bu ana kadar beklenir (arka plan işi sorar); <c>null</c> = beklenen yanıt yok
    /// (temel senaryo, e-Arşiv, e-SMM ya da yanıt geldi / süre doldu).
    /// </summary>
    public DateTime? BuyerAnswerDueUtc { get; private set; }

    public DateTime? BuyerRejectedAtUtc { get; private set; }
    public string? BuyerRejectionReason { get; private set; }

    public string? DiscardReason { get; private set; }
    public Guid? DiscardedByUserId { get; private set; }
    public DateTime? DiscardedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = default!;

    /// <summary>Belge ücretleri tutuyor mu: iptal edilen ve vazgeçilen belge ücretleri serbest bırakır.</summary>
    public bool HoldsCharges => Status is not (EDocumentStatus.Discarded or EDocumentStatus.Cancelled or EDocumentStatus.RejectedByBuyer);

    /// <summary>Kesilmiş belge alıcının kabul / red yanıtını bekliyor mu.</summary>
    public bool AwaitsBuyerAnswer => Status == EDocumentStatus.Issued && BuyerAnswerDueUtc is not null;

    public DateTime? CancellableUntilUtc => IssuedAtUtc?.AddDays(EDocumentRules.CancelWindowDays);

    public bool HasProviderDocument => ProviderDocumentId is not null;

    private EDocument() { }

    public static Result<EDocument> Create(
        Guid tenantId,
        Guid clinicId,
        Guid clientId,
        Guid? saleId,
        Guid? visitId,
        EDocumentType type,
        EDocumentEnvironment environment,
        string providerCode,
        Guid requestId,
        string requestHash,
        string? note,
        EDocumentBuyer buyer,
        IReadOnlyList<EDocumentLineDraft> lines,
        decimal? withholdingRate,
        Guid createdByUserId,
        DateTime utcNow,
        EDocumentOrigin origin = EDocumentOrigin.Manual)
    {
        if (tenantId == Guid.Empty || clinicId == Guid.Empty || clientId == Guid.Empty)
            return Invalid("Kiracı, klinik ve müşteri zorunludur.");
        if (requestId == Guid.Empty)
            return Result<EDocument>.Failure("EDocuments.RequestIdRequired", "requestId zorunludur (çift istek koruması).");
        if (createdByUserId == Guid.Empty)
            return Invalid("Oluşturan kullanıcı geçersiz.");
        if (string.IsNullOrWhiteSpace(providerCode) || providerCode.Length > MaxProviderCodeLength)
            return Invalid("Sağlayıcı geçersiz.");
        if (string.IsNullOrWhiteSpace(requestHash) || requestHash.Length > MaxRequestHashLength)
            return Invalid("İstek özeti geçersiz.");
        if (!Enum.IsDefined(type) || !Enum.IsDefined(environment) || !Enum.IsDefined(origin))
            return Invalid("Belge türü veya ortam geçersiz.");
        if (lines.Count == 0)
            return Invalid("Belgede en az bir satır olmalıdır.");
        if (lines.Select(l => l.ChargeId).Distinct().Count() > EDocumentRules.MaxChargesPerDocument)
            return Invalid($"Bir belgeye en çok {EDocumentRules.MaxChargesPerDocument} ücret bağlanabilir.");
        if (lines.Any(l => l.GrossAmount <= 0 || l.Quantity <= 0 || string.IsNullOrWhiteSpace(l.Description)
                           || !TaxRatePercent.IsValid(l.VatRate)))
            return Invalid("Belge satırı geçersiz (tutar, miktar, açıklama veya KDV).");
        if (!TaxRatePercent.IsValidOptional(withholdingRate))
            return Invalid(TaxRatePercent.InvalidMessage);

        var noteText = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (noteText is { Length: > EDocumentRules.MaxNoteLength })
            return Invalid($"Not en fazla {EDocumentRules.MaxNoteLength} karakter olabilir.");

        var document = new EDocument
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            ClientId = clientId,
            SaleId = saleId,
            VisitId = visitId,
            Type = type,
            Status = EDocumentStatus.Draft,
            Environment = environment,
            Origin = origin,
            ProviderCode = providerCode,
            ExternalReference = Guid.NewGuid(),
            RequestId = requestId,
            RequestHash = requestHash,
            Note = noteText,
            Buyer = buyer,
            WithholdingRate = EDocumentRules.AppliesWithholding(type, buyer.Kind) ? withholdingRate : null,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = utcNow,
        };

        for (var i = 0; i < lines.Count; i++)
            document._lines.Add(new EDocumentLine(document.Id, i + 1, lines[i]));
        document.TotalAmount = document._lines.Sum(l => l.GrossAmount);
        return Result<EDocument>.Success(document);
    }

    /// <summary>İlk gönderim (<c>Draft</c>) veya kullanıcı yeniden denemesi (<c>Failed</c>).</summary>
    public Result BeginSending(DateTime utcNow)
    {
        if (Status is not (EDocumentStatus.Draft or EDocumentStatus.Failed))
            return InvalidState("Yalnız taslak veya hatalı belge gönderilebilir.");

        Status = EDocumentStatus.Sending;
        AttemptCount++;
        CheckCount = 0;
        NextCheckAtUtc = null;
        ClearError();
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    public void RecordProviderInvoice(string providerInvoiceId)
        => ProviderInvoiceId = Trim(providerInvoiceId, MaxProviderIdLength);

    /// <summary>Sağlayıcı sonucu asenkron üretiyor; belge <c>Sending</c> kalır, arka plan işi <paramref name="nextCheckAtUtc"/> zamanında sorar.</summary>
    public Result AwaitProviderResult(string? providerJobId, DateTime nextCheckAtUtc, DateTime utcNow)
    {
        if (Status != EDocumentStatus.Sending)
            return InvalidState("Yalnız gönderilen belge sonuç bekleyebilir.");

        if (providerJobId is not null)
            ProviderJobId = Trim(providerJobId, MaxProviderIdLength);
        NextCheckAtUtc = nextCheckAtUtc;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    public Result MarkIssued(
        string providerDocumentId,
        string? documentNumber,
        string? documentUuid,
        decimal? providerTotal,
        DateTime utcNow)
    {
        if (Status != EDocumentStatus.Sending)
            return InvalidState("Yalnız gönderilen belge kesildi olarak işaretlenebilir.");
        if (string.IsNullOrWhiteSpace(providerDocumentId))
            return Result.Failure("EDocuments.Validation", "Sağlayıcı belge kimliği zorunludur.");

        Status = EDocumentStatus.Issued;
        ProviderDocumentId = Trim(providerDocumentId, MaxProviderIdLength);
        DocumentNumber = TrimOptional(documentNumber, MaxDocumentNumberLength);
        DocumentUuid = TrimOptional(documentUuid, MaxProviderIdLength);
        ProviderTotal = providerTotal;
        IssuedAtUtc = utcNow;
        NextCheckAtUtc = null;
        ClearError();
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>
    /// Geçici hata (ağ, hız sınırı, 5xx): belge bulunduğu bekleme durumunda (<c>Sending</c> / <c>CancelRequested</c>) kalır,
    /// arka plan işi <paramref name="nextCheckAtUtc"/> zamanında yeniden dener.
    /// </summary>
    public Result RecordTransientFailure(
        string message, string? providerErrorCode, DateTime nextCheckAtUtc, DateTime utcNow)
    {
        if (Status is not (EDocumentStatus.Sending or EDocumentStatus.CancelRequested))
            return InvalidState("Geçici hata yalnız bekleyen belgede kaydedilir.");

        SetError(EDocumentErrorKind.Transient, message, providerErrorCode);
        NextCheckAtUtc = nextCheckAtUtc;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>Kalıcı gönderim hatası; kullanıcı düzeltip yeniden dener veya vazgeçer.</summary>
    public Result MarkFailed(EDocumentErrorKind kind, string message, string? providerErrorCode, DateTime utcNow)
    {
        if (Status != EDocumentStatus.Sending)
            return InvalidState("Yalnız gönderilen belge hatalı olarak işaretlenebilir.");

        Status = EDocumentStatus.Failed;
        SetError(kind, message, providerErrorCode);
        NextCheckAtUtc = null;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>Arka plan kontrolü sayacı; işlemci artan aralığı ve üst sınırı bununla hesaplar.</summary>
    public void CountCheck() => CheckCount++;

    public Result Discard(Guid userId, string? reason, DateTime utcNow)
    {
        if (Status != EDocumentStatus.Failed)
            return InvalidState("Yalnız hatalı belgeden vazgeçilebilir.");
        if (HasProviderDocument)
            return InvalidState("Sağlayıcıda kesilmiş belgeden vazgeçilemez; iptal edin.");
        var text = ValidateReason(reason);
        if (!text.IsSuccess)
            return Result.Failure(text.Error);
        if (userId == Guid.Empty)
            return Result.Failure("EDocuments.Validation", "Kullanıcı geçersiz.");

        Status = EDocumentStatus.Discarded;
        DiscardReason = text.Value;
        DiscardedByUserId = userId;
        DiscardedAtUtc = utcNow;
        NextCheckAtUtc = null;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    public Result RequestCancel(Guid userId, string? reason, DateTime utcNow)
    {
        if (Status is not (EDocumentStatus.Issued or EDocumentStatus.CancelRejected))
            return InvalidState("Yalnız kesilmiş belge iptal edilebilir.");
        if (CancellableUntilUtc is { } until && utcNow > until)
        {
            return Result.Failure(
                "EDocuments.CancelWindowExpired",
                $"İptal süresi ({EDocumentRules.CancelWindowDays} gün) doldu; düzeltme için iade belgesi gerekir.");
        }

        var text = ValidateReason(reason);
        if (!text.IsSuccess)
            return Result.Failure(text.Error);
        if (userId == Guid.Empty)
            return Result.Failure("EDocuments.Validation", "Kullanıcı geçersiz.");

        Status = EDocumentStatus.CancelRequested;
        CancelReason = text.Value;
        CancelRequestedByUserId = userId;
        CancelRequestedAtUtc = utcNow;
        CheckCount = 0;
        NextCheckAtUtc = null;
        ClearError();
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    public Result MarkCancelled(DateTime utcNow)
    {
        if (Status != EDocumentStatus.CancelRequested)
            return InvalidState("Yalnız iptali istenen belge iptal edildi olarak işaretlenebilir.");

        Status = EDocumentStatus.Cancelled;
        CancelledAtUtc = utcNow;
        NextCheckAtUtc = null;
        ClearError();
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>Kesilen ticari e-Fatura alıcı yanıtını bekler; arka plan işi <paramref name="nextCheckAtUtc"/> zamanında sorar.</summary>
    public Result AwaitBuyerAnswer(DateTime dueUtc, DateTime nextCheckAtUtc, DateTime utcNow)
    {
        if (Status != EDocumentStatus.Issued)
            return InvalidState("Yalnız kesilmiş belge alıcı yanıtı bekleyebilir.");

        BuyerAnswerDueUtc = dueUtc;
        NextCheckAtUtc = nextCheckAtUtc;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>Yanıt henüz yok (veya sorulamadı): bir sonraki soruş zamanı.</summary>
    public Result PostponeBuyerAnswerCheck(DateTime nextCheckAtUtc, DateTime utcNow)
    {
        if (!AwaitsBuyerAnswer)
            return InvalidState("Belge alıcı yanıtı beklemiyor.");

        NextCheckAtUtc = nextCheckAtUtc;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>Alıcı kabul etti ya da yanıt süresi doldu: belge kesilmiş kalır, beklenen yanıt yok.</summary>
    public Result CloseBuyerAnswer(DateTime utcNow)
    {
        if (!AwaitsBuyerAnswer)
            return InvalidState("Belge alıcı yanıtı beklemiyor.");

        BuyerAnswerDueUtc = null;
        NextCheckAtUtc = null;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>Alıcı ticari e-Faturayı reddetti: belge geçersiz, ücretler serbest kalır (çağıran bağı çözer).</summary>
    public Result MarkRejectedByBuyer(string? reason, DateTime utcNow)
    {
        if (!AwaitsBuyerAnswer)
            return InvalidState("Yalnız alıcı yanıtı bekleyen belge reddedilmiş olarak işaretlenebilir.");

        Status = EDocumentStatus.RejectedByBuyer;
        BuyerRejectedAtUtc = utcNow;
        BuyerRejectionReason = TrimOptional(reason, EDocumentRules.MaxReasonLength);
        BuyerAnswerDueUtc = null;
        NextCheckAtUtc = null;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    public Result MarkCancelRejected(string message, string? providerErrorCode, DateTime utcNow)
    {
        if (Status != EDocumentStatus.CancelRequested)
            return InvalidState("Yalnız iptali istenen belge reddedildi olarak işaretlenebilir.");

        Status = EDocumentStatus.CancelRejected;
        SetError(EDocumentErrorKind.ProviderRejected, message, providerErrorCode);
        NextCheckAtUtc = null;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    private void SetError(EDocumentErrorKind kind, string message, string? providerErrorCode)
    {
        LastErrorKind = kind;
        LastErrorMessage = Trim(string.IsNullOrWhiteSpace(message) ? "Sağlayıcı hatası." : message, MaxErrorMessageLength);
        LastProviderErrorCode = TrimOptional(providerErrorCode, MaxProviderErrorCodeLength);
    }

    private void ClearError()
    {
        LastErrorKind = null;
        LastErrorMessage = null;
        LastProviderErrorCode = null;
    }

    private static Result<string> ValidateReason(string? reason)
    {
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text))
            return Result<string>.Failure("EDocuments.Validation", "Gerekçe zorunludur.");
        if (text.Length > EDocumentRules.MaxReasonLength)
            return Result<string>.Failure("EDocuments.Validation", $"Gerekçe en fazla {EDocumentRules.MaxReasonLength} karakter olabilir.");
        return Result<string>.Success(text);
    }

    private static string Trim(string value, int max)
    {
        var text = value.Trim();
        return text.Length > max ? text[..max] : text;
    }

    private static string? TrimOptional(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : Trim(value, max);

    private static Result InvalidState(string message) => Result.Failure(InvalidStateCode, message);

    private static Result<EDocument> Invalid(string message) => Result<EDocument>.Failure("EDocuments.Validation", message);
}
