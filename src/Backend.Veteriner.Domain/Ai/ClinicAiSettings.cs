using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Ai;

/// <summary>
/// Kliniğin AI özellikleri ayarı. Kayıt yoksa <b>kapalı</b> sayılır (<see cref="IsEnabledOrDefault"/>). Klinik yöneticisi kendi
/// kliniğinde açar ve kapatır; açmadan önce güncel bilgilendirmeyi kabul eder (<see cref="SetEnabledByClinic"/>, ADR-044 E1).
/// Platform yöneticisi kabulsüz açıp kapatabilir (<see cref="SetEnabled"/>; deneme ve demo klinikleri).
/// </summary>
public sealed class ClinicAiSettings : ITenantOwned
{
    /// <summary>Klinik açmak isterken güncel bilgilendirme kabul edilmemiş.</summary>
    public static readonly Error AcknowledgementRequired =
        new("Ai.AcknowledgementRequired", "Yapay zekâyı açmak için bilgilendirmeyi okuyup kabul edin.");

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }

    /// <summary>Son kota uyarısının verildiği ayın başı (UTC, İstanbul takvim ayı); uyarı ayda her eşik için bir kez gider.</summary>
    public DateTime? QuotaWarningMonthStartUtc { get; private set; }

    /// <summary>O ay verilen en yüksek uyarı eşiği (%; 0, 80 ya da 100).</summary>
    public int QuotaWarningLevel { get; private set; }

    /// <summary>Kabul edilen en son bilgilendirme sürümü; hiç kabul edilmediyse <c>null</c>.</summary>
    public int? AcknowledgedVersion { get; private set; }

    public DateTime? AcknowledgedAtUtc { get; private set; }
    public Guid? AcknowledgedByUserId { get; private set; }

    private ClinicAiSettings() { }

    public static ClinicAiSettings Create(Guid tenantId, Guid clinicId, DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));

        return new ClinicAiSettings { TenantId = tenantId, ClinicId = clinicId, IsEnabled = false, CreatedAtUtc = utcNow };
    }

    /// <summary>Kayıt yoksa kapalı.</summary>
    public static bool IsEnabledOrDefault(ClinicAiSettings? settings) => settings?.IsEnabled ?? false;

    /// <summary>Bu sürüm ya da daha yenisi kabul edilmiş mi.</summary>
    public bool HasAcknowledged(int version) => AcknowledgedVersion >= version;

    /// <summary>
    /// Klinik yöneticisinin açması/kapatması. Açmak için <paramref name="requiredVersion"/> daha önce kabul edilmiş olmalı ya da bu
    /// istekte kabul edilmeli (<paramref name="acceptedVersion"/>); kabul kimin ve ne zaman yaptığıyla kaydedilir. Kapatmak kabul
    /// istemez ve veri silmez.
    /// </summary>
    public Result SetEnabledByClinic(bool enabled, int requiredVersion, int? acceptedVersion, Guid userId, DateTime utcNow)
    {
        if (requiredVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(requiredVersion), "Bilgilendirme sürümü pozitif olmalıdır.");
        if (userId == Guid.Empty)
            throw new ArgumentException("Kullanıcı geçersiz.", nameof(userId));

        if (enabled && !HasAcknowledged(requiredVersion))
        {
            if (acceptedVersion != requiredVersion)
                return Result.Failure(AcknowledgementRequired);

            AcknowledgedVersion = requiredVersion;
            AcknowledgedAtUtc = utcNow;
            AcknowledgedByUserId = userId;
        }

        SetEnabled(enabled, userId, utcNow);
        return Result.Success();
    }

    /// <summary>Platform yöneticisinin açması/kapatması; bilgilendirme kabulü istemez.</summary>
    public void SetEnabled(bool enabled, Guid? userId, DateTime utcNow)
    {
        IsEnabled = enabled;
        UpdatedAtUtc = utcNow;
        UpdatedByUserId = userId;
    }

    /// <summary>
    /// Ayın kota uyarısını işaretler (ADR-044 madde 7: %80'de ve bitişte bir kez). O ay aynı ya da daha yüksek eşik verildiyse
    /// <c>false</c> (uyarı gönderilmez); %80 atlanıp doğrudan %100'e çıkılırsa yalnız bitiş uyarısı gider.
    /// </summary>
    public bool TryMarkQuotaWarning(DateTime monthStartUtc, int level)
    {
        if (level <= 0)
            return false;

        var current = QuotaWarningMonthStartUtc == monthStartUtc ? QuotaWarningLevel : 0;
        if (level <= current)
            return false;

        QuotaWarningMonthStartUtc = monthStartUtc;
        QuotaWarningLevel = level;
        return true;
    }
}
