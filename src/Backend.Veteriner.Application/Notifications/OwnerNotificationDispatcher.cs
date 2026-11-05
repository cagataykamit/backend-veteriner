using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Notifications;
using Mediator;
using Microsoft.Extensions.Logging;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Notifications;

/// <summary>
/// Hesap düzeyi push gönderimi (PORTAL-010; MOBILE-003): bildirimi outbox'a çağıranın işlemiyle aynı commit'te yazar, cihazlara
/// teslim <see cref="OwnerPushDelivery"/> ile arka planda yapılır. Sağlayıcı (<see cref="IPushNotificationProvider"/>) yapılandırılmadıysa
/// <see cref="IsConfigured"/> <c>false</c> döner ve hiçbir şey yazılmaz. Sağlayıcı değiştirilebilir (ADR-031 K8).
/// </summary>
public interface IPushNotificationSender
{
    bool IsConfigured { get; }

    /// <summary>Hesabın cihazlarına bildirim kuyruğa alındıysa <c>true</c>. Metin asgari veri taşır (K7).</summary>
    Task<bool> SendAsync(OwnerNotificationType type, Guid ownerAccountId, string title, string body, CancellationToken ct);
}

/// <summary>Hasta sahibine giden tek bildirim; kanal tercihleri çağıran tarafından doldurulur.</summary>
/// <param name="Email">Hesabın e-postası; silinmiş hesapta boş (e-posta gitmez).</param>
/// <param name="RecipientOptedIn">Tür kategorisinin tercihi: hatırlatmada müşteri kaydı, ticaride bağdaki onay; işlemselde yok sayılır.</param>
public sealed record OwnerNotificationMessage(
    OwnerNotificationType Type,
    Guid OwnerAccountId,
    string? Email,
    bool PushEnabled,
    bool RecipientOptedIn,
    string Subject,
    string Body,
    string PushTitle,
    string PushBody);

/// <summary>Hangi kanaldan gittiği (denetim ve test için).</summary>
public sealed record OwnerNotificationDispatchResult(bool EmailQueued, bool PushQueued);

/// <summary>
/// Hasta sahibi bildirimini tür × kanal kuralına (<see cref="OwnerNotificationChannelPolicy"/>) göre gönderir: e-posta ve push
/// outbox'a çağıranın işlemiyle aynı commit'te yazılır. WhatsApp sistem kanalı değildir (personel bağlantısı).
/// </summary>
public interface IOwnerNotificationDispatcher
{
    Task<OwnerNotificationDispatchResult> DispatchAsync(OwnerNotificationMessage message, CancellationToken ct);
}

public sealed class OwnerNotificationDispatcher : IOwnerNotificationDispatcher
{
    private readonly IEmailSender _email;
    private readonly IPushNotificationSender _push;
    private readonly ILogger<OwnerNotificationDispatcher> _logger;

    public OwnerNotificationDispatcher(IEmailSender email, IPushNotificationSender push, ILogger<OwnerNotificationDispatcher> logger)
    {
        _email = email;
        _push = push;
        _logger = logger;
    }

    public async Task<OwnerNotificationDispatchResult> DispatchAsync(OwnerNotificationMessage message, CancellationToken ct)
    {
        var emailQueued = false;
        if (!string.IsNullOrWhiteSpace(message.Email)
            && OwnerNotificationChannelPolicy.ShouldSend(message.Type, OwnerNotificationChannel.Email, _push.IsConfigured, message.RecipientOptedIn))
        {
            await _email.SendAsync(message.Email, message.Subject, message.Body, ct);
            emailQueued = true;
        }

        var pushQueued = false;
        var pushOptIn = message.PushEnabled
                        && (OwnerNotificationChannelPolicy.CategoryOf(message.Type) == OwnerNotificationCategory.Transactional || message.RecipientOptedIn);
        if (OwnerNotificationChannelPolicy.ShouldSend(message.Type, OwnerNotificationChannel.Push, _push.IsConfigured, pushOptIn))
        {
            // Push en iyi çabadır: başarısızlık işlemi bozmaz, e-posta asıl kanaldır.
            try
            {
                pushQueued = await _push.SendAsync(message.Type, message.OwnerAccountId, message.PushTitle, message.PushBody, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Push bildirimi kuyruğa alınamadı. Type={Type} OwnerAccountId={OwnerAccountId}", message.Type, message.OwnerAccountId);
            }
        }

        return new OwnerNotificationDispatchResult(emailQueued, pushQueued);
    }
}

public sealed record OwnerNotificationChannelRuleDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] OwnerNotificationChannel Channel,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] OwnerNotificationChannelMode Mode);

public sealed record OwnerNotificationRuleDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] OwnerNotificationType Type,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] OwnerNotificationCategory Category,
    IReadOnlyList<OwnerNotificationChannelRuleDto> Channels);

/// <summary>Tür × kanal matrisi (panelde salt okunur gösterim; PORTAL-010).</summary>
public sealed record GetPortalNotificationRulesQuery : IRequest<Result<IReadOnlyList<OwnerNotificationRuleDto>>>;

public sealed class GetPortalNotificationRulesQueryHandler
    : IRequestHandler<GetPortalNotificationRulesQuery, Result<IReadOnlyList<OwnerNotificationRuleDto>>>
{
    private readonly IPushNotificationSender _push;

    public GetPortalNotificationRulesQueryHandler(IPushNotificationSender push) => _push = push;

    public ValueTask<Result<IReadOnlyList<OwnerNotificationRuleDto>>> Handle(GetPortalNotificationRulesQuery request, CancellationToken ct)
    {
        IReadOnlyList<OwnerNotificationRuleDto> rows = OwnerNotificationChannelPolicy.AllTypes
            .Select(t => new OwnerNotificationRuleDto(
                t,
                OwnerNotificationChannelPolicy.CategoryOf(t),
                OwnerNotificationChannelPolicy.AllChannels
                    .Select(c => new OwnerNotificationChannelRuleDto(c, OwnerNotificationChannelPolicy.ModeOf(t, c, _push.IsConfigured)))
                    .ToList()))
            .ToList();
        return ValueTask.FromResult(Result<IReadOnlyList<OwnerNotificationRuleDto>>.Success(rows));
    }
}
