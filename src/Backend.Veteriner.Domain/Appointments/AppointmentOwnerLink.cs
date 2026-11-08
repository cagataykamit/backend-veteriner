using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Appointments;

/// <summary>Hatırlatma bağlantısıyla sahibin yaptığı eylem (TIER-019).</summary>
public enum AppointmentOwnerLinkAction
{
    Confirm = 0,
    Cancel = 1
}

/// <summary>Bağlantının amacı (TIER-020): yanıt bağlantısı takvim dosyasını da verir; takvim bağlantısı yalnız dosyayı verir.</summary>
public enum AppointmentOwnerLinkPurpose
{
    Respond = 0,
    Calendar = 1,

    /// <summary>
    /// Bekleme listesi teklifi (TIER-018): boşalan saati kabul bağlantısı. <see cref="AppointmentOwnerLink.AppointmentId"/> saati
    /// boşaltan (iptal edilmiş) randevudur; teklif <see cref="AppointmentOwnerLink.WaitlistOfferId"/>'dedir. Randevu eylemi ve takvim
    /// dosyası vermez.
    /// </summary>
    WaitlistOffer = 2
}

/// <summary>
/// Randevu hatırlatmasındaki "onayla / iptal et" bağlantısı (TIER-019). Tek bağlantı iki eylemi de taşır; ham token yalnız
/// e-postada bulunur, depoda SHA-256 özeti tutulur. Süre randevu saatinde dolar ve tek kullanımlıktır: bir eylem bağlantıyı
/// kapatır, aynı randevunun diğer bağlantıları da kapanır. Randevu yeniden zamanlanırsa bağlantı geçersizdir (saat eşleşmez).
/// "Takvime ekle" (TIER-020) aynı tokeni kullanır: takvim dosyası okumadır, tek kullanım kuralı ona uygulanmaz.
/// </summary>
public sealed class AppointmentOwnerLink : ITenantOwned
{
    public const int TokenHashLength = 64;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid AppointmentId { get; private set; }
    public string TokenHash { get; private set; } = default!;

    /// <summary>Takvim bağlantısı adreste (GET) taşındığı için eylem yapamaz; eylem yalnız yanıt bağlantısıyladır.</summary>
    public AppointmentOwnerLinkPurpose Purpose { get; private set; }

    /// <summary>Bekleme listesi teklifi (yalnız <see cref="AppointmentOwnerLinkPurpose.WaitlistOffer"/>).</summary>
    public Guid? WaitlistOfferId { get; private set; }

    /// <summary>Gönderim anındaki randevu saati; bağlantı bu ana kadar ve randevu bu saatte kaldıkça geçerlidir.</summary>
    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }

    /// <summary>Bu bağlantıyla yapılan eylem; başka bağlantının eylemiyle kapanan bağlantıda boştur.</summary>
    public AppointmentOwnerLinkAction? UsedAction { get; private set; }

    private AppointmentOwnerLink() { }

    public static AppointmentOwnerLink Create(
        Guid tenantId,
        Guid clinicId,
        Guid appointmentId,
        string tokenHash,
        DateTime appointmentStartUtc,
        DateTime utcNow,
        AppointmentOwnerLinkPurpose purpose = AppointmentOwnerLinkPurpose.Respond)
    {
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (appointmentId == Guid.Empty)
            throw new ArgumentException("AppointmentId geçersiz.", nameof(appointmentId));
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("Token özeti boş olamaz.", nameof(tokenHash));

        var now = ToUtc(utcNow);
        var expires = ToUtc(appointmentStartUtc);
        if (expires <= now)
            throw new ArgumentException("Bağlantı yalnız gelecek randevu için oluşturulur.", nameof(appointmentStartUtc));

        return new AppointmentOwnerLink
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            AppointmentId = appointmentId,
            TokenHash = tokenHash,
            Purpose = purpose,
            ExpiresAtUtc = expires,
            CreatedAtUtc = now,
        };
    }

    /// <summary>
    /// Bekleme listesi teklifinin kabul bağlantısı (TIER-018). Süre teklifin bitişidir; saati boşaltan randevuya bağlanır.
    /// </summary>
    public static AppointmentOwnerLink CreateForWaitlistOffer(
        Guid tenantId,
        Guid clinicId,
        Guid sourceAppointmentId,
        Guid waitlistOfferId,
        string tokenHash,
        DateTime offerExpiresAtUtc,
        DateTime utcNow)
    {
        if (waitlistOfferId == Guid.Empty)
            throw new ArgumentException("WaitlistOfferId geçersiz.", nameof(waitlistOfferId));

        var link = Create(tenantId, clinicId, sourceAppointmentId, tokenHash, offerExpiresAtUtc, utcNow, AppointmentOwnerLinkPurpose.WaitlistOffer);
        link.WaitlistOfferId = waitlistOfferId;
        return link;
    }

    /// <summary>Teklif bağlantısı açık ve süresi dolmamış (TIER-018); teklifin kendi durumu ayrıca denetlenir.</summary>
    public bool IsUsableForWaitlistOffer(DateTime utcNow)
        => Purpose == AppointmentOwnerLinkPurpose.WaitlistOffer && ClosedAtUtc is null && ToUtc(utcNow) < ExpiresAtUtc;

    /// <summary>
    /// Eylem (onayla / iptal et) için kullanılabilir: yanıt bağlantısı, kapanmamış ve <see cref="IsCalendarUsableFor"/>.
    /// </summary>
    public bool IsUsableFor(Appointment appointment, DateTime utcNow)
        => Purpose == AppointmentOwnerLinkPurpose.Respond
           && ClosedAtUtc is null
           && IsCalendarUsableFor(appointment, utcNow);

    /// <summary>
    /// Takvim dosyası için kullanılabilir (her iki amaç): bu randevunun bağlantısı, randevu hâlâ gönderimdeki saatte ve sahip
    /// yanıtı alabilir (<see cref="Appointment.CanOwnerRespond"/>; saat geçmişse süre de dolmuştur). Onayla kapanan bağlantı
    /// takvim dosyasını vermeye devam eder; iptal edilen ya da yeniden zamanlanan randevuda geçersizdir.
    /// </summary>
    public bool IsCalendarUsableFor(Appointment appointment, DateTime utcNow)
        => Purpose != AppointmentOwnerLinkPurpose.WaitlistOffer
           && appointment.Id == AppointmentId
           && appointment.TenantId == TenantId
           && appointment.ScheduledAtUtc == ExpiresAtUtc
           && appointment.CanOwnerRespond(utcNow);

    /// <summary>Bu bağlantıyla eylem yapıldı; bağlantı kapanır.</summary>
    public void MarkUsed(AppointmentOwnerLinkAction action, DateTime utcNow)
    {
        if (!Enum.IsDefined(action))
            throw new ArgumentOutOfRangeException(nameof(action));

        UsedAction = action;
        Close(utcNow);
    }

    /// <summary>Aynı randevunun başka bağlantısıyla eylem yapıldı; bu bağlantı da kapanır.</summary>
    public void Close(DateTime utcNow) => ClosedAtUtc ??= ToUtc(utcNow);

    /// <summary>Randevu saatiyle aynı kural (<c>Appointment.NormalizeUtc</c>): veritabanından türü belirsiz okunan değer UTC'dir.</summary>
    private static DateTime ToUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
