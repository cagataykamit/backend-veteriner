using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.Notifications;
using Backend.Veteriner.Domain.OwnerPortal;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Notifications;

/// <summary>Tek cihaza teslim sonucu.</summary>
public enum PushDeliveryOutcome
{
    Delivered = 0,

    /// <summary>Jeton geçersiz ya da uygulama kaldırılmış (FCM <c>UNREGISTERED</c> vb.); cihaz kaydı silinir.</summary>
    InvalidToken = 1,

    /// <summary>Geçici ya da yapılandırma hatası; jeton korunur.</summary>
    Failed = 2,
}

/// <summary>Cihaza gidecek içerik; <see cref="Type"/> istemcinin yönlendirmesi için veri alanında taşınır.</summary>
public sealed record PushMessage(OwnerNotificationType Type, string Title, string Body);

/// <summary>
/// Push sağlayıcısı (MOBILE-003): tek cihaza gönderir. FCM HTTP v1 yalnız yapılandırma anahtarı varken etkindir; yoksa
/// yapılandırılmamış sağlayıcı yalnız log yazar (<see cref="IsConfigured"/> <c>false</c>). Android, iOS (APNs, Firebase üzerinden) ve
/// web aynı sağlayıcıdan geçer.
/// </summary>
public interface IPushNotificationProvider
{
    bool IsConfigured { get; }

    Task<PushDeliveryOutcome> SendAsync(OwnerPushPlatform platform, string deviceToken, PushMessage message, CancellationToken ct);
}

/// <summary>Outbox'taki push mesajı (<c>OutboxMessageTypes.OwnerPush</c>). Cihaz jetonu taşımaz; teslimde okunur.</summary>
public sealed class OwnerPushOutboxPayload
{
    public required Guid OwnerAccountId { get; init; }
    public required OwnerNotificationType Type { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
}

/// <summary>
/// Outbox'tan gelen push'u hesabın cihazlarına teslim eder (MOBILE-003). Tercih teslim anında yeniden okunur (kuyruktayken
/// kapatıldıysa ya da hesap silindiyse gönderilmez). Geçersiz jetonlar silinir. Hiçbir cihaza ulaşılamadıysa ve en az bir geçici
/// hata varsa istisna fırlatır; outbox yeniden dener. Kısmi başarıda yeniden denenmez (ulaşan cihaza ikinci kez gitmesin).
/// </summary>
public sealed class OwnerPushDelivery
{
    private readonly IReadRepository<OwnerAccount> _accounts;
    private readonly IRepository<OwnerPushDevice> _devices;
    private readonly IPushNotificationProvider _provider;
    private readonly ILogger<OwnerPushDelivery> _logger;

    public OwnerPushDelivery(
        IReadRepository<OwnerAccount> accounts,
        IRepository<OwnerPushDevice> devices,
        IPushNotificationProvider provider,
        ILogger<OwnerPushDelivery> logger)
    {
        _accounts = accounts;
        _devices = devices;
        _provider = provider;
        _logger = logger;
    }

    public async Task DeliverAsync(OwnerPushOutboxPayload payload, CancellationToken ct)
    {
        if (!_provider.IsConfigured)
        {
            _logger.LogInformation("Push sağlayıcısı yapılandırılmadı; bildirim gönderilmedi. Type={Type} OwnerAccountId={OwnerAccountId}",
                payload.Type, payload.OwnerAccountId);
            return;
        }

        var account = await _accounts.FirstOrDefaultAsync(new OwnerAccountByIdSpec(payload.OwnerAccountId), ct);
        if (account is not { IsActive: true, PushEnabled: true })
            return;

        var devices = await _devices.ListAsync(new OwnerPushDevicesByAccountSpec(account.Id), ct);
        var message = new PushMessage(payload.Type, payload.Title, payload.Body);
        var delivered = 0;
        var failed = 0;
        var invalid = new List<OwnerPushDevice>();
        foreach (var device in devices)
        {
            switch (await _provider.SendAsync(device.Platform, device.Token, message, ct))
            {
                case PushDeliveryOutcome.Delivered:
                    delivered++;
                    break;
                case PushDeliveryOutcome.InvalidToken:
                    invalid.Add(device);
                    break;
                default:
                    failed++;
                    break;
            }
        }

        if (invalid.Count > 0)
        {
            await _devices.DeleteRangeAsync(invalid, ct);
            await _devices.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Push teslim edildi. Type={Type} OwnerAccountId={OwnerAccountId} Delivered={Delivered} InvalidRemoved={Invalid} Failed={Failed}",
            payload.Type, account.Id, delivered, invalid.Count, failed);

        if (delivered == 0 && failed > 0)
            throw new InvalidOperationException($"Push hiçbir cihaza ulaşmadı ({failed} geçici hata); yeniden denenecek.");
    }
}
