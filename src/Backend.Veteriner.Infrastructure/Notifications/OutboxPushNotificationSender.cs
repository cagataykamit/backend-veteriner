using System.Text.Json;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Domain.Notifications;

namespace Backend.Veteriner.Infrastructure.Notifications;

/// <summary>
/// Push'u outbox'a yazar (e-postadaki <c>TransactionalEmailSender</c> deseni): çağıranın işlemiyle aynı commit, teslim ve yeniden
/// deneme outbox işlemcisinde (<see cref="OwnerPushDelivery"/>). Sağlayıcı yapılandırılmadıysa hiçbir şey yazmaz.
/// </summary>
public sealed class OutboxPushNotificationSender : IPushNotificationSender
{
    private readonly IOutboxBuffer _buffer;
    private readonly IPushNotificationProvider _provider;

    public OutboxPushNotificationSender(IOutboxBuffer buffer, IPushNotificationProvider provider)
    {
        _buffer = buffer;
        _provider = provider;
    }

    public bool IsConfigured => _provider.IsConfigured;

    public async Task<bool> SendAsync(OwnerNotificationType type, Guid ownerAccountId, string title, string body, CancellationToken ct)
    {
        if (!IsConfigured)
            return false;

        var payload = new OwnerPushOutboxPayload { OwnerAccountId = ownerAccountId, Type = type, Title = title, Body = body };
        await _buffer.EnqueueAsync(OutboxMessageTypes.OwnerPush, JsonSerializer.Serialize(payload), ct);
        return true;
    }
}
