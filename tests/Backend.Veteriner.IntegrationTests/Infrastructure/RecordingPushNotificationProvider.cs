using System.Collections.Concurrent;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Domain.OwnerPortal;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Outbox işlemci testlerinde FCM yerine geçer (yapılandırılmış sayılır): jeton başına gönderimi kaydeder, jetona bağlanan sonucu
/// döner (varsayılan <see cref="PushDeliveryOutcome.Delivered"/>).
/// </summary>
internal sealed class RecordingPushNotificationProvider : IPushNotificationProvider
{
    private readonly ConcurrentDictionary<string, PushDeliveryOutcome> _outcomes = new();
    private readonly ConcurrentQueue<(string Token, PushMessage Message)> _sent = new();

    public bool IsConfigured => true;

    public IReadOnlyList<(string Token, PushMessage Message)> Sent => _sent.ToList();

    public void OnSend(string token, PushDeliveryOutcome outcome) => _outcomes[token] = outcome;

    public Task<PushDeliveryOutcome> SendAsync(OwnerPushPlatform platform, string deviceToken, PushMessage message, CancellationToken ct)
    {
        _sent.Enqueue((deviceToken, message));
        return Task.FromResult(_outcomes.GetValueOrDefault(deviceToken, PushDeliveryOutcome.Delivered));
    }
}
