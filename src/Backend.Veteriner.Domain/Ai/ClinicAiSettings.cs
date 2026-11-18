using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Ai;

/// <summary>
/// Kliniğin AI özellikleri ayarı (ADR-044 "az sayıda klinikle deneme"). Kayıt yoksa <b>kapalı</b> sayılır
/// (<see cref="IsEnabledOrDefault"/>); açma/kapama yalnız platform yöneticisindedir (maliyet Vetinity'nin API hesabına yazılır).
/// </summary>
public sealed class ClinicAiSettings : ITenantOwned
{
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
