using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Domain.OwnerPortal;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Notifications;

/// <summary>
/// Push anahtarı (<c>Push:Fcm</c>) yokken kullanılan sağlayıcı (MOBILE-003): gönderim yapmaz, yalnız log yazar. Anahtar eklenince
/// açılışta FCM sağlayıcısı seçilir; çağıranlar etkilenmez.
/// </summary>
public sealed class NotConfiguredPushNotificationProvider : IPushNotificationProvider
{
    private readonly ILogger<NotConfiguredPushNotificationProvider> _logger;

    public NotConfiguredPushNotificationProvider(ILogger<NotConfiguredPushNotificationProvider> logger) => _logger = logger;

    public bool IsConfigured => false;

    public Task<PushDeliveryOutcome> SendAsync(OwnerPushPlatform platform, string deviceToken, PushMessage message, CancellationToken ct)
    {
        _logger.LogDebug("Push sağlayıcısı yapılandırılmadı; bildirim gönderilmedi. Type={Type} Platform={Platform}", message.Type, platform);
        return Task.FromResult(PushDeliveryOutcome.Failed);
    }
}
