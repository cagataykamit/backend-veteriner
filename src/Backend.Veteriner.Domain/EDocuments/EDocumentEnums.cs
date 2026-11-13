namespace Backend.Veteriner.Domain.EDocuments;

/// <summary>e-belge türü (ADR-036). Tür <see cref="EDocumentTypeRule"/> ile belirlenir.</summary>
public enum EDocumentType
{
    ESmm = 0,
    EArchive = 1,
    EInvoice = 2
}

/// <summary>Belge durumu (ADR-036 ilke 4). Geçişler yalnız <see cref="EDocument"/> içinde.</summary>
public enum EDocumentStatus
{
    Draft = 0,
    Sending = 1,
    Issued = 2,
    Failed = 3,
    Discarded = 4,
    CancelRequested = 5,
    Cancelled = 6,
    CancelRejected = 7,

    /// <summary>Ticari e-Fatura alıcı tarafından reddedildi; belge geçersiz, ücretler serbest (yeniden belgelenebilir).</summary>
    RejectedByBuyer = 8
}

/// <summary>Sağlayıcı hatasının sınıfı; adaptör çevirir, kullanıcıya Türkçe metinle döner.</summary>
public enum EDocumentErrorKind
{
    Unknown = 0,
    InvalidData = 1,
    Authorization = 2,
    AccountSuspended = 3,
    QuotaExceeded = 4,
    ProviderRejected = 5,
    Transient = 6
}

/// <summary>Sağlayıcı ortamı (ADR-036 ilke 7); test belgesi gerçek değildir.</summary>
public enum EDocumentEnvironment
{
    Test = 0,
    Live = 1
}

/// <summary>Alıcı türü: kurumsal (vergi no), bireysel (TCKN) veya kimliği bilinmeyen bireysel alıcı.</summary>
public enum EDocumentBuyerKind
{
    Individual = 0,
    Corporate = 1
}

/// <summary>Belgeyi başlatan: kullanıcı elle, ödeme ekranında "e-Belge kes" ile veya otomatik kesme ayarıyla (ADR-036 AK-4, 2026-11-11).</summary>
public enum EDocumentOrigin
{
    Manual = 0,
    Payment = 1,
    Automatic = 2
}

/// <summary>Klinik otomatik e-belge ayarı: kapalı, ödeme alınınca (tamamen ödenen ücret) veya gün sonunda.</summary>
public enum EDocumentAutoIssueMode
{
    Off = 0,
    OnPayment = 1,
    EndOfDay = 2
}
