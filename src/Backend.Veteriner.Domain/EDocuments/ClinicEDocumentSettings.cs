using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.EDocuments;

/// <summary>
/// Klinik e-belge ayarı (ADR-036 AK-4, kullanıcı kararı 2026-11-11): otomatik kesme modu, gün sonu saati (İstanbul saati) ve ödeme
/// ekranında "e-Belge kes" seçeneğinin varsayılanı. Sağlayıcı bağlantısından ayrıdır (bağlantı koparılsa da ayar kalır).
/// Kayıt yoksa: otomatik kapalı, gün sonu 23:00, ödeme ekranı seçeneği işaretsiz.
/// </summary>
public sealed class ClinicEDocumentSettings : ITenantOwned
{
    public static readonly TimeOnly DefaultEndOfDayTime = new(23, 0);

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public EDocumentAutoIssueMode AutoIssueMode { get; private set; }

    /// <summary>Gün sonu çalışma saati ve gün sonu hatırlatma bildirimi saati (İstanbul).</summary>
    public TimeOnly EndOfDayTime { get; private set; } = DefaultEndOfDayTime;

    /// <summary>Ödeme ekranındaki "e-Belge kes" seçeneği varsayılan işaretli mi.</summary>
    public bool IssueOnPaymentByDefault { get; private set; }

    /// <summary>
    /// Otomatik kesmenin açıldığı an: yalnız bu andan sonra yazılan ücretler otomatik belgelenir (ayarı açmak geçmiş ücretlerin
    /// hepsini birden kesmez). Mod kapatılınca boşalır.
    /// </summary>
    public DateTime? AutoIssueEnabledAtUtc { get; private set; }

    /// <summary>Gün sonu işinin son çalıştığı İstanbul günü (aynı gün ikinci kez çalışmaz).</summary>
    public DateOnly? LastEndOfDayRunDate { get; private set; }

    /// <summary>
    /// Otomatik kesme kalıcı bir sağlayıcı sorunuyla (yetki / anahtar, kontör, hesap askıda, bağlantı kullanılamıyor) durduruldu:
    /// her yeni ücret aynı hatayla "kesilemedi" olmasın diye yeni belge kesilmez. Ayar kaydedilince ("Sürdür") yeniden başlar.
    /// </summary>
    public DateTime? AutoIssuePausedAtUtc { get; private set; }

    public string? AutoIssuePauseReason { get; private set; }

    public const int MaxPauseReasonLength = 500;

    public bool IsAutoIssuePaused => AutoIssuePausedAtUtc is not null;

    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>Gün sonu işaretini iki örnek aynı anda yazarsa biri kaybeder.</summary>
    public byte[] RowVersion { get; private set; } = default!;

    private ClinicEDocumentSettings() { }

    public static ClinicEDocumentSettings Create(Guid tenantId, Guid clinicId, Guid userId, DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));

        return new ClinicEDocumentSettings
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            UpdatedByUserId = userId,
            CreatedAtUtc = utcNow,
        };
    }

    public Result Update(EDocumentAutoIssueMode mode, TimeOnly endOfDayTime, bool issueOnPaymentByDefault, Guid userId, DateTime utcNow)
    {
        if (!Enum.IsDefined(mode))
            return Result.Failure("EDocuments.Validation", "Geçersiz otomatik e-belge ayarı.");
        if (userId == Guid.Empty)
            return Result.Failure("EDocuments.Validation", "Kaydeden kullanıcı geçersiz.");

        if (mode == EDocumentAutoIssueMode.Off)
            AutoIssueEnabledAtUtc = null;
        else if (AutoIssueMode == EDocumentAutoIssueMode.Off || AutoIssueEnabledAtUtc is null)
            AutoIssueEnabledAtUtc = utcNow;

        AutoIssueMode = mode;
        EndOfDayTime = new TimeOnly(endOfDayTime.Hour, endOfDayTime.Minute);
        IssueOnPaymentByDefault = issueOnPaymentByDefault;
        UpdatedByUserId = userId;
        UpdatedAtUtc = utcNow;
        // Kaydetmek durdurulmuş otomatik kesmeyi sürdürür (sorun giderilmediyse ilk turda yeniden durur).
        AutoIssuePausedAtUtc = null;
        AutoIssuePauseReason = null;
        return Result.Success();
    }

    /// <summary>Otomatik kesmeyi kalıcı hatada durdurur. Kapalıysa veya zaten durmuşsa <c>false</c> (bildirim bir kez gider).</summary>
    public bool PauseAutoIssue(string reason, DateTime utcNow)
    {
        if (AutoIssueMode == EDocumentAutoIssueMode.Off || IsAutoIssuePaused)
            return false;

        var text = string.IsNullOrWhiteSpace(reason) ? "Entegratör hesabında kalıcı bir sorun var." : reason.Trim();
        AutoIssuePausedAtUtc = utcNow;
        AutoIssuePauseReason = text.Length > MaxPauseReasonLength ? text[..MaxPauseReasonLength] : text;
        return true;
    }

    /// <summary>Gün sonu işinin bu gün için çalışması gerekiyor mu (saat geldi ve bugün çalışmadı).</summary>
    public bool IsEndOfDayDue(DateOnly localDate, TimeOnly localTime)
        => localTime >= EndOfDayTime && LastEndOfDayRunDate != localDate;

    public void MarkEndOfDayRun(DateOnly localDate) => LastEndOfDayRunDate = localDate;
}
