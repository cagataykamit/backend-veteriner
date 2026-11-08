using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Clinics;

/// <summary>
/// Klinik bazlı randevu varsayılan ayarları.
/// Tenant + Clinic benzersizdir.
/// </summary>
public sealed class ClinicAppointmentSettings : ITenantOwned
{
    /// <summary>Hatırlatmadaki "iptal et" bağlantısı varsayılan açık (TIER-019; çalışma varsayımı). Kayıt yoksa da açıktır.</summary>
    public const bool DefaultOwnerCancelLinkEnabled = true;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public int DefaultAppointmentDurationMinutes { get; private set; }
    public int SlotIntervalMinutes { get; private set; }
    public bool AllowOverlappingAppointments { get; private set; }

    /// <summary>
    /// Randevu hatırlatmasında sahibe "iptal et" bağlantısı gider (TIER-019). Kapalıysa e-postada "iptal için kliniği arayın"
    /// satırı çıkar ve bağlantıdan iptal reddedilir; "onayla" her durumda açıktır.
    /// </summary>
    public bool OwnerCancelLinkEnabled { get; private set; } = DefaultOwnerCancelLinkEnabled;

    /// <summary>Bekleme listesinde boşalan saate otomatik teklif varsayılan açık (TIER-018; çalışma varsayımı). Kayıt yoksa da açıktır.</summary>
    public const bool DefaultWaitlistAutoOfferEnabled = true;

    /// <summary>
    /// Randevu iptal edilince bekleme listesindeki sıradaki uygun kayda otomatik teklif gider (TIER-018). Kapalıysa personel teklifi
    /// elle gönderir.
    /// </summary>
    public bool WaitlistAutoOfferEnabled { get; private set; } = DefaultWaitlistAutoOfferEnabled;

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private ClinicAppointmentSettings() { }

    public static ClinicAppointmentSettings Create(
        Guid tenantId,
        Guid clinicId,
        int defaultAppointmentDurationMinutes,
        int slotIntervalMinutes,
        bool allowOverlappingAppointments,
        DateTime utcNow,
        bool ownerCancelLinkEnabled = DefaultOwnerCancelLinkEnabled,
        bool waitlistAutoOfferEnabled = DefaultWaitlistAutoOfferEnabled)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));

        Validate(defaultAppointmentDurationMinutes, slotIntervalMinutes);

        return new ClinicAppointmentSettings
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            DefaultAppointmentDurationMinutes = defaultAppointmentDurationMinutes,
            SlotIntervalMinutes = slotIntervalMinutes,
            AllowOverlappingAppointments = allowOverlappingAppointments,
            OwnerCancelLinkEnabled = ownerCancelLinkEnabled,
            WaitlistAutoOfferEnabled = waitlistAutoOfferEnabled,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = null,
        };
    }

    public void Update(
        int defaultAppointmentDurationMinutes,
        int slotIntervalMinutes,
        bool allowOverlappingAppointments,
        bool ownerCancelLinkEnabled,
        DateTime utcNow,
        bool? waitlistAutoOfferEnabled = null)
    {
        Validate(defaultAppointmentDurationMinutes, slotIntervalMinutes);
        DefaultAppointmentDurationMinutes = defaultAppointmentDurationMinutes;
        SlotIntervalMinutes = slotIntervalMinutes;
        AllowOverlappingAppointments = allowOverlappingAppointments;
        OwnerCancelLinkEnabled = ownerCancelLinkEnabled;
        WaitlistAutoOfferEnabled = waitlistAutoOfferEnabled ?? WaitlistAutoOfferEnabled;
        UpdatedAtUtc = utcNow;
    }

    private static void Validate(int duration, int interval)
    {
        if (duration < 5 || duration > 240)
            throw new ArgumentException("DefaultAppointmentDurationMinutes 5-240 aralığında olmalıdır.");
        if (interval < 5 || interval > 120)
            throw new ArgumentException("SlotIntervalMinutes 5-120 aralığında olmalıdır.");
    }
}
