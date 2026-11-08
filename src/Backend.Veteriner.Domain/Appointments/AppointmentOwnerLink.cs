using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Appointments;

/// <summary>Hatırlatma bağlantısıyla sahibin yaptığı eylem (TIER-019).</summary>
public enum AppointmentOwnerLinkAction
{
    Confirm = 0,
    Cancel = 1
}

/// <summary>
/// Randevu hatırlatmasındaki "onayla / iptal et" bağlantısı (TIER-019). Tek bağlantı iki eylemi de taşır; ham token yalnız
/// e-postada bulunur, depoda SHA-256 özeti tutulur. Süre randevu saatinde dolar ve tek kullanımlıktır: bir eylem bağlantıyı
/// kapatır, aynı randevunun diğer bağlantıları da kapanır. Randevu yeniden zamanlanırsa bağlantı geçersizdir (saat eşleşmez).
/// </summary>
public sealed class AppointmentOwnerLink : ITenantOwned
{
    public const int TokenHashLength = 64;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid AppointmentId { get; private set; }
    public string TokenHash { get; private set; } = default!;

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
        DateTime utcNow)
    {
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
            ExpiresAtUtc = expires,
            CreatedAtUtc = now,
        };
    }

    /// <summary>
    /// Kullanılabilir: kapanmamış, bu randevunun bağlantısı, randevu hâlâ gönderimdeki saatte ve sahip yanıtı alabilir
    /// (<see cref="Appointment.CanOwnerRespond"/>; saat geçmişse süre de dolmuştur).
    /// </summary>
    public bool IsUsableFor(Appointment appointment, DateTime utcNow)
        => ClosedAtUtc is null
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
