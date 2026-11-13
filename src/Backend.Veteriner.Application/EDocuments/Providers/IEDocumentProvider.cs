using Backend.Veteriner.Domain.EDocuments;

namespace Backend.Veteriner.Application.EDocuments.Providers;

/// <summary>
/// e-belge entegratörü (ADR-036 ilke 1). Her entegratör bir uygulamadır; domain'de entegratör adı geçmez, seçim
/// bağlantının <see cref="EDocumentConnection.ProviderCode"/> değeriyledir. Çağrılar kliniğin kendi hesabıyla yapılır
/// (<see cref="EDocumentProviderAccount"/>); yenilenen kimlik bilgisi sonuçla döner, çağıran şifreleyip saklar.
/// </summary>
public interface IEDocumentProvider
{
    string ProviderCode { get; }
    string DisplayName { get; }

    /// <summary>Sunucuda uygulama kimliği tanımlı mı (değilse bağlanma ve gönderim yapılamaz).</summary>
    bool IsConfigured { get; }

    EDocumentEnvironment Environment { get; }

    Task<ProviderResult<ProviderAccountCheck>> VerifyAsync(EDocumentProviderAccount account, CancellationToken ct);

    /// <summary>Vergi numarası e-Fatura mükellefiyse gelen kutusu adresi; değilse <c>null</c>.</summary>
    Task<ProviderResult<string?>> FindEInvoiceRecipientAsync(EDocumentProviderAccount account, string taxNumber, CancellationToken ct);

    /// <summary>Belgeyi gönderir. Aynı <see cref="EDocumentSendRequest.ExternalReference"/> ile tekrar çağrı çift belge üretmez.</summary>
    Task<ProviderResult<ProviderIssueOutcome>> IssueAsync(EDocumentProviderAccount account, EDocumentSendRequest request, CancellationToken ct);

    /// <summary>Asenkron sonucu sorar (iş bitti mi, belge oluştu mu).</summary>
    Task<ProviderResult<ProviderIssueOutcome>> CheckAsync(EDocumentProviderAccount account, EDocumentProviderRefs refs, CancellationToken ct);

    /// <summary>Bu belge türü entegratörün API'sinden iptal edilebilir mi (Nilvera'da e-Fatura iptali yok).</summary>
    bool CanCancel(EDocumentType type);

    /// <summary>
    /// Kesilmiş ticari e-Faturanın alıcı yanıtı (kabul / red / bekliyor). Yalnız kesim sonucu
    /// <see cref="ProviderIssueOutcome.AwaitsBuyerAnswer"/> olan belgede sorulur.
    /// </summary>
    Task<ProviderResult<ProviderBuyerAnswer>> GetBuyerAnswerAsync(EDocumentProviderAccount account, EDocumentProviderRefs refs, CancellationToken ct);

    Task<ProviderResult<ProviderCancelOutcome>> CancelAsync(EDocumentProviderAccount account, EDocumentProviderRefs refs, CancellationToken ct);

    /// <summary>Belgesi oluşmamış taslak kaydı sağlayıcıda geri alır (vazgeçme); kayıt yoksa başarı.</summary>
    Task<ProviderResult<bool>> DiscardDraftAsync(EDocumentProviderAccount account, EDocumentProviderRefs refs, CancellationToken ct);

    /// <summary>PDF; hazır değilse <see cref="ProviderPdf.NotReady"/>.</summary>
    Task<ProviderResult<ProviderPdf>> GetPdfAsync(EDocumentProviderAccount account, EDocumentProviderRefs refs, CancellationToken ct);
}

/// <summary>OAuth ile bağlanan entegratörün yetkilendirme adımları (Paraşüt). API anahtarı kullanan entegratör bunu uygulamaz.</summary>
public interface IEDocumentOAuthProvider
{
    string ProviderCode { get; }

    /// <summary>Tarayıcının yönlendirileceği yetki adresi (PKCE S256).</summary>
    string BuildAuthorizationUrl(string state, string codeChallenge);

    /// <summary>Yetki kodunu kimlik bilgisine (sağlayıcıya özel, şifrelenecek metin) çevirir.</summary>
    Task<ProviderResult<string>> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct);

    /// <summary>Kullanıcının erişebildiği hesaplar (Paraşüt firmaları).</summary>
    Task<ProviderResult<IReadOnlyList<ProviderAccountInfo>>> ListAccountsAsync(string credentials, CancellationToken ct);
}

/// <summary>
/// API anahtarıyla bağlanan entegratör (Nilvera): klinik kendi hesabında ürettiği anahtarı girer; anahtar sağlayıcıya özel
/// kimlik bilgisidir, şifreli saklanır. OAuth kullanan entegratör bunu uygulamaz.
/// </summary>
public interface IEDocumentCredentialProvider
{
    string ProviderCode { get; }

    /// <summary>Anahtarla sağlayıcıya bağlanıp anahtarın ait olduğu hesabı döndürür (geçersiz anahtar <see cref="EDocumentErrorKind.Authorization"/>).</summary>
    Task<ProviderResult<ProviderAccountInfo>> IdentifyAsync(string credentials, CancellationToken ct);
}

public sealed record EDocumentProviderAccount(string AccountId, string Credentials);

public sealed record ProviderAccountInfo(string AccountId, string Name, string? TaxNumber);

/// <param name="EDocumentsEnabled">Hesapta e-belge kullanımı açık mı; bilinmiyorsa <c>null</c>.</param>
public sealed record ProviderAccountCheck(string AccountName, bool? EDocumentsEnabled);

public sealed record ProviderError(EDocumentErrorKind Kind, string Message, string? ProviderCode = null)
{
    public bool IsTransient => Kind == EDocumentErrorKind.Transient;
}

/// <summary>Sağlayıcı çağrısı sonucu; <see cref="RefreshedCredentials"/> doluysa çağıran saklar (başarı veya hata fark etmez).</summary>
public sealed record ProviderResult<T>(T? Value, ProviderError? Error, string? RefreshedCredentials)
{
    public bool IsSuccess => Error is null;

    public static ProviderResult<T> Ok(T value, string? refreshedCredentials = null) => new(value, null, refreshedCredentials);

    public static ProviderResult<T> Fail(ProviderError error, string? refreshedCredentials = null) => new(default, error, refreshedCredentials);
}

public sealed record EDocumentSendLine(string Description, decimal Quantity, string? Unit, decimal NetUnitPrice, decimal VatRate);

public sealed record EDocumentSendBuyer(
    EDocumentBuyerKind Kind,
    string Name,
    string IdentityNumber,
    bool IsUnknownBuyer,
    string? TaxOffice,
    string? Province,
    string? District,
    string? AddressLine,
    string? Email);

/// <param name="IssuedAtLocal">Düzenlenme anı, İstanbul saati (belgenin oluşturulduğu an; yeniden denemede değişmez).</param>
/// <param name="EInvoiceRecipient">e-Fatura'da alıcının gelen kutusu adresi.</param>
/// <param name="ExistingInvoiceId">Önceki denemede oluşan sağlayıcı fatura kaydı (yeniden denemede).</param>
public sealed record EDocumentSendRequest(
    EDocumentType Type,
    Guid ExternalReference,
    DateTime IssuedAtLocal,
    EDocumentSendBuyer Buyer,
    IReadOnlyList<EDocumentSendLine> Lines,
    decimal? WithholdingRate,
    string? Note,
    string? EInvoiceRecipient,
    string? ExistingInvoiceId)
{
    /// <summary>Düzenlenme günü (İstanbul).</summary>
    public DateOnly IssueDate => DateOnly.FromDateTime(IssuedAtLocal);
}

public sealed record EDocumentProviderRefs(
    EDocumentType Type,
    Guid ExternalReference,
    string? ProviderInvoiceId,
    string? ProviderDocumentId,
    string? ProviderJobId);

public enum ProviderIssueState
{
    Issued = 0,
    Pending = 1
}

public sealed record ProviderIssueOutcome(
    ProviderIssueState State,
    string? ProviderInvoiceId,
    string? ProviderDocumentId,
    string? ProviderJobId,
    string? DocumentNumber,
    string? DocumentUuid,
    decimal? Total,
    bool AwaitsBuyerAnswer = false)
{
    public static ProviderIssueOutcome Pending(string? invoiceId, string? jobId) => new(ProviderIssueState.Pending, invoiceId, null, jobId, null, null, null);
}

public enum ProviderCancelState
{
    Cancelled = 0,
    Pending = 1
}

public sealed record ProviderCancelOutcome(ProviderCancelState State);

public enum ProviderBuyerAnswerState
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2
}

/// <param name="Reason">Alıcının red gerekçesi (varsa).</param>
public sealed record ProviderBuyerAnswer(ProviderBuyerAnswerState State, string? Reason = null)
{
    public static ProviderBuyerAnswer Pending { get; } = new(ProviderBuyerAnswerState.Pending);
    public static ProviderBuyerAnswer Accepted { get; } = new(ProviderBuyerAnswerState.Accepted);
}

public sealed record ProviderPdf(byte[]? Content, string FileName)
{
    public bool IsReady => Content is not null;

    public static ProviderPdf NotReady { get; } = new(null, "");
}
