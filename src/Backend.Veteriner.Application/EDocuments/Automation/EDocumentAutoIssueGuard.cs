using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.EDocuments.Specs;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Domain.EDocuments;

namespace Backend.Veteriner.Application.EDocuments.Automation;

/// <summary>
/// Otomatik kesmeyi hesap genelindeki kalıcı sağlayıcı sorununda durdurur (ADR-036): yetki / anahtar reddi, hesap askıda veya
/// kontör bitti, kota, bağlantı kullanılamıyor. Durmazsa her yeni ücret aynı hatayla "kesilemedi" olur ve kontör/istek harcanır.
/// Belgeye özgü hatalar (alıcı bilgisi, verinin reddi) durdurmaz. Klinik ayarını değiştirebilenlere bir kez bildirilir; ayar
/// kaydedilince otomatik kesme sürer. Kayıt (SaveChanges) çağıranın işidir.
/// </summary>
public sealed class EDocumentAutoIssueGuard
{
    private readonly IRepository<ClinicEDocumentSettings> _settings;
    private readonly IUserNotificationPublisher _notifications;
    private readonly TimeProvider _timeProvider;

    public EDocumentAutoIssueGuard(
        IRepository<ClinicEDocumentSettings> settings, IUserNotificationPublisher notifications, TimeProvider timeProvider)
    {
        _settings = settings;
        _notifications = notifications;
        _timeProvider = timeProvider;
    }

    /// <summary>Hata tek belgeye değil hesabın tamamına mı ait (sonraki belgeler de aynı hatayı alır).</summary>
    public static bool IsAccountWide(EDocumentErrorKind? kind)
        => kind is EDocumentErrorKind.Authorization or EDocumentErrorKind.AccountSuspended or EDocumentErrorKind.QuotaExceeded;

    /// <summary>Otomatik kesme açıksa ve durmamışsa durdurur ve bildirir; durdurduysa <c>true</c>.</summary>
    public async Task<bool> PauseAsync(Guid tenantId, Guid clinicId, string? reason, CancellationToken ct)
    {
        var settings = await _settings.FirstOrDefaultAsync(new ClinicEDocumentSettingsByClinicSpec(tenantId, clinicId), ct);
        if (settings is null || !settings.PauseAutoIssue(reason ?? string.Empty, _timeProvider.GetUtcNow().UtcDateTime))
            return false;

        await _notifications.EDocumentsAutoIssuePausedAsync(tenantId, clinicId, settings.AutoIssuePauseReason!, ct);
        return true;
    }
}
