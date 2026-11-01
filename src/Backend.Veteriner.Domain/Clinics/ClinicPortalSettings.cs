using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Clinics;

/// <summary>
/// Kliniğin hasta sahibi portalı ayarı (PORTAL-009; ADR-031 K8 çıkış planı). Kayıt yoksa portal açık sayılır (<see cref="IsEnabledOrDefault"/>).
/// Kapatınca hesaplar ve bağlar silinmez; yeni davet gitmez, bağ kapsamlı portal uçları kapanır. Tekrar açınca her şey geri gelir.
/// </summary>
public sealed class ClinicPortalSettings : ITenantOwned
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }

    private ClinicPortalSettings() { }

    public static ClinicPortalSettings Create(Guid tenantId, Guid clinicId, DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));

        return new ClinicPortalSettings { TenantId = tenantId, ClinicId = clinicId, IsEnabled = true, CreatedAtUtc = utcNow };
    }

    /// <summary>Kayıt yoksa açık (bugünkü davranış korunur).</summary>
    public static bool IsEnabledOrDefault(ClinicPortalSettings? settings) => settings?.IsEnabled ?? true;

    public void SetEnabled(bool enabled, Guid userId, DateTime utcNow)
    {
        IsEnabled = enabled;
        UpdatedAtUtc = utcNow;
        UpdatedByUserId = userId;
    }
}
