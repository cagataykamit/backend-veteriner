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
}
