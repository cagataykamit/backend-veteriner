using Backend.Veteriner.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Notifications;

/// <summary>
/// Push sağlayıcısı yapılandırılmadan kullanılan uygulama (PORTAL-010): FCM/APNs anahtarı dış bağımlılıktır (MOBILE-003). Gönderim
/// yapmaz; sağlayıcı eklendiğinde bu sınıf gerçek uygulamayla değiştirilir, çağıranlar etkilenmez.
/// </summary>
public sealed class NotConfiguredPushNotificationSender : IPushNotificationSender
{
    private readonly ILogger<NotConfiguredPushNotificationSender> _logger;

    public NotConfiguredPushNotificationSender(ILogger<NotConfiguredPushNotificationSender> logger) => _logger = logger;

    public bool IsConfigured => false;

    public Task<bool> SendAsync(Guid ownerAccountId, string title, string body, CancellationToken ct)
    {
        _logger.LogDebug("Push sağlayıcısı yapılandırılmadı; bildirim gönderilmedi. OwnerAccountId={OwnerAccountId}", ownerAccountId);
        return Task.FromResult(false);
    }
}
