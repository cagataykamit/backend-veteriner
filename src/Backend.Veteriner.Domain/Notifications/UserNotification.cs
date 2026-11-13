using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Notifications;

/// <summary>Bildirim türü (ADR-042 K5; UX-008). Sayısal değerler stabildir.</summary>
public enum UserNotificationType
{
    /// <summary>Yeni online randevu talebi (talep kuyruğu yetkisi olanlara).</summary>
    AppointmentRequestCreated = 0,

    /// <summary>Sahip bekleyen talebini iptal etti.</summary>
    AppointmentRequestCancelled = 1,

    /// <summary>Kişiye atanan iş: randevu bir hekime atandı.</summary>
    AppointmentAssigned = 2,

    /// <summary>Hasta sahibi portaldan KVKK veri talebi gönderdi (PORTAL-007/009; portal yönetim yetkisi olanlara).</summary>
    PortalDataRequestCreated = 3,

    /// <summary>Sahip randevuyu hatırlatma e-postasındaki bağlantıdan iptal etti (TIER-019; randevu iptal yetkisi olanlara).</summary>
    AppointmentCancelledByOwner = 4,

    /// <summary>Sahip bekleme listesi teklifini kabul etti, randevu oluştu (TIER-018; randevu oluşturma yetkisi olanlara).</summary>
    WaitlistOfferAccepted = 5,

    /// <summary>Tüm veri dışa aktarma paketi hazır (talep edene; DATA_EXPORT_API_CONTRACT.md).</summary>
    DataExportReady = 6,

    /// <summary>Tüm veri dışa aktarma paketi hazırlanamadı (talep edene).</summary>
    DataExportFailed = 7,

    /// <summary>Gün sonu e-belge özeti: otomatik kesilen, kesilemeyen ve belgesiz ödenmiş ücretler (ADR-036; belge kesme izni olanlara).</summary>
    EDocumentsDailySummary = 8,

    /// <summary>Otomatik e-belge kalıcı bir sağlayıcı sorunuyla (yetki, kontör, bağlantı) durduruldu (ADR-036; klinik ayarını değiştirebilenlere).</summary>
    EDocumentsAutoIssuePaused = 9,

    /// <summary>Belgesi kesilmiş ücretin ödemesi iptal edildi; belge kendiliğinden iptal edilmez (ADR-036; belge iptal yetkisi olanlara).</summary>
    EDocumentPaymentVoided = 10,

    /// <summary>Ticari e-Fatura alıcı tarafından reddedildi; ücretler serbest kaldı (ADR-036; belge kesme izni olanlara).</summary>
    EDocumentRejectedByBuyer = 11,
}

/// <summary>
/// Kullanıcıya kalıcı bildirim (APPT-015, UX-008): kime, hangi olay, hangi kayıt, okundu mu. Yalnız olay tabanlı ve kişiye yönelik
/// olaylar buraya girer; kaynak kayıttan hesaplanan açık işler listesinin yerini almaz (ASAMA8 D-N1/D-N2).
/// </summary>
public sealed class UserNotification : ITenantOwned
{
    public const int TitleMaxLength = 200;
    public const int BodyMaxLength = 500;
    public const int TargetTypeMaxLength = 50;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid? ClinicId { get; private set; }
    public Guid UserId { get; private set; }
    public UserNotificationType Type { get; private set; }
    public string Title { get; private set; } = default!;
    public string? Body { get; private set; }

    /// <summary>Tıklanınca açılacak kayıt türü (ör. <c>AppointmentRequest</c>, <c>Appointment</c>).</summary>
    public string? TargetType { get; private set; }

    public Guid? TargetId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }

    public bool IsRead => ReadAtUtc is not null;

    private UserNotification() { }

    public static UserNotification Create(
        Guid tenantId,
        Guid? clinicId,
        Guid userId,
        UserNotificationType type,
        string title,
        string? body,
        string? targetType,
        Guid? targetId,
        DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId geçersiz.", nameof(userId));
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Başlık boş olamaz.", nameof(title));

        return new UserNotification
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            UserId = userId,
            Type = type,
            Title = Truncate(title.Trim(), TitleMaxLength),
            Body = string.IsNullOrWhiteSpace(body) ? null : Truncate(body.Trim(), BodyMaxLength),
            TargetType = targetType,
            TargetId = targetId,
            CreatedAtUtc = utcNow,
        };
    }

    /// <summary>Okundu işaretler; zaten okunduysa değişiklik yok (<c>false</c>).</summary>
    public bool MarkRead(DateTime utcNow)
    {
        if (ReadAtUtc is not null)
            return false;

        ReadAtUtc = utcNow;
        return true;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
