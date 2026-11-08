using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Notifications;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.OwnerPortal.Preferences;

/// <summary>Hesap düzeyi tercih (docs/PORTAL_API_CONTRACT.md §10.1, §14); <see cref="DeviceCount"/> kayıtlı push cihazı sayısı.</summary>
public sealed record PortalPushPreferenceDto(bool Enabled, bool Available, int DeviceCount);

public sealed record PortalAccountNotificationPreferencesDto(PortalPushPreferenceDto Push);

/// <summary>Bağ düzeyi tercih (§10.2): hatırlatma (müşteri kaydı) ve ticari ileti onayı (bağ).</summary>
public sealed record PortalLinkNotificationPreferencesDto(
    bool RemindersOptedOut,
    bool CanEnableReminders,
    bool MarketingConsented,
    DateTime? MarketingConsentedAtUtc,
    bool MarketingSendingEnabled);

public sealed record GetPortalAccountNotificationPreferencesQuery(Guid OwnerAccountId)
    : IRequest<Result<PortalAccountNotificationPreferencesDto>>;

public sealed record UpdatePortalAccountNotificationPreferencesCommand(Guid OwnerAccountId, bool? PushEnabled)
    : IRequest<Result<PortalAccountNotificationPreferencesDto>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "Portal.NotificationPreferences.Update";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId}";
}

public sealed record GetPortalLinkNotificationPreferencesQuery(Guid OwnerAccountId, Guid LinkId)
    : IRequest<Result<PortalLinkNotificationPreferencesDto>>, IAuditableRequest
{
    public string AuditAction => "Portal.Read.NotificationPreferences";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId};LinkId={LinkId}";
}

/// <summary>Kısmi güncelleme: <c>null</c> alan dokunulmaz; ikisi de boşsa 400. Kiracı salt okunur olsa da sahip tercihini değiştirir.</summary>
public sealed record UpdatePortalLinkNotificationPreferencesCommand(Guid OwnerAccountId, Guid LinkId, bool? RemindersOptedOut, bool? MarketingConsented)
    : IRequest<Result<PortalLinkNotificationPreferencesDto>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "Portal.NotificationPreferences.Update";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId};LinkId={LinkId}";
}

public sealed class UpdatePortalAccountNotificationPreferencesCommandValidator : AbstractValidator<UpdatePortalAccountNotificationPreferencesCommand>
{
    public UpdatePortalAccountNotificationPreferencesCommandValidator()
        => RuleFor(x => x.PushEnabled).NotNull().WithMessage("pushEnabled zorunludur.");
}

public sealed class UpdatePortalLinkNotificationPreferencesCommandValidator : AbstractValidator<UpdatePortalLinkNotificationPreferencesCommand>
{
    public UpdatePortalLinkNotificationPreferencesCommandValidator()
        => RuleFor(x => x).Must(x => x.RemindersOptedOut.HasValue || x.MarketingConsented.HasValue)
            .WithMessage("En az bir tercih gönderilmelidir.");
}

internal static class PortalNotificationPreferencesMapping
{
    public static PortalAccountNotificationPreferencesDto ToDto(OwnerAccount account, IPushNotificationSender push, int deviceCount)
        => new(new PortalPushPreferenceDto(account.PushEnabled, push.IsConfigured, deviceCount));

    public static PortalLinkNotificationPreferencesDto ToDto(Client client, OwnerAccountClientLink link)
        => new(
            client.RemindersOptedOut,
            client.OwnerCanEnableReminders,
            link.MarketingConsentAtUtc.HasValue,
            link.MarketingConsentAtUtc,
            OwnerNotificationChannelPolicy.MarketingSendingEnabled);
}

public sealed class GetPortalAccountNotificationPreferencesQueryHandler
    : IRequestHandler<GetPortalAccountNotificationPreferencesQuery, Result<PortalAccountNotificationPreferencesDto>>
{
    private readonly OwnerPortalAccess _access;
    private readonly IReadRepository<OwnerPushDevice> _devices;
    private readonly IPushNotificationSender _push;

    public GetPortalAccountNotificationPreferencesQueryHandler(
        OwnerPortalAccess access,
        IReadRepository<OwnerPushDevice> devices,
        IPushNotificationSender push)
    {
        _access = access;
        _devices = devices;
        _push = push;
    }

    public async ValueTask<Result<PortalAccountNotificationPreferencesDto>> Handle(GetPortalAccountNotificationPreferencesQuery request, CancellationToken ct)
    {
        var account = await _access.GetActiveAccountAsync(request.OwnerAccountId, ct);
        if (!account.IsSuccess)
            return Result<PortalAccountNotificationPreferencesDto>.Failure(account.Error);

        var deviceCount = await _devices.CountAsync(new OwnerPushDevicesByAccountSpec(request.OwnerAccountId), ct);
        return Result<PortalAccountNotificationPreferencesDto>.Success(PortalNotificationPreferencesMapping.ToDto(account.Value!, _push, deviceCount));
    }
}

public sealed class UpdatePortalAccountNotificationPreferencesCommandHandler
    : IRequestHandler<UpdatePortalAccountNotificationPreferencesCommand, Result<PortalAccountNotificationPreferencesDto>>
{
    private readonly IRepository<OwnerAccount> _accounts;
    private readonly IRepository<OwnerPushDevice> _devices;
    private readonly IPushNotificationSender _push;

    public UpdatePortalAccountNotificationPreferencesCommandHandler(
        IRepository<OwnerAccount> accounts,
        IRepository<OwnerPushDevice> devices,
        IPushNotificationSender push)
    {
        _accounts = accounts;
        _devices = devices;
        _push = push;
    }

    public async ValueTask<Result<PortalAccountNotificationPreferencesDto>> Handle(UpdatePortalAccountNotificationPreferencesCommand request, CancellationToken ct)
    {
        var account = await _accounts.FirstOrDefaultAsync(new OwnerAccountByIdSpec(request.OwnerAccountId), ct);
        if (account is not { IsActive: true })
            return Result<PortalAccountNotificationPreferencesDto>.Failure(OwnerPortalErrors.AccountUnavailable);

        var devicesSpec = new OwnerPushDevicesByAccountSpec(account.Id);
        if (account.SetPushEnabled(request.PushEnabled!.Value))
        {
            // Veri azaltma: tercih kapanınca cihaz jetonları tutulmaz; yeniden açılınca istemci cihazı yeniden kaydeder (§14).
            if (!account.PushEnabled)
                await _devices.DeleteRangeAsync(devicesSpec, ct);
            await _accounts.SaveChangesAsync(ct);
        }

        var deviceCount = account.PushEnabled ? await _devices.CountAsync(devicesSpec, ct) : 0;
        return Result<PortalAccountNotificationPreferencesDto>.Success(PortalNotificationPreferencesMapping.ToDto(account, _push, deviceCount));
    }
}

public sealed class GetPortalLinkNotificationPreferencesQueryHandler
    : IRequestHandler<GetPortalLinkNotificationPreferencesQuery, Result<PortalLinkNotificationPreferencesDto>>
{
    private readonly OwnerPortalAccess _access;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<OwnerAccountClientLink> _links;

    public GetPortalLinkNotificationPreferencesQueryHandler(
        OwnerPortalAccess access,
        IReadRepository<Client> clients,
        IReadRepository<OwnerAccountClientLink> links)
    {
        _access = access;
        _clients = clients;
        _links = links;
    }

    public async ValueTask<Result<PortalLinkNotificationPreferencesDto>> Handle(GetPortalLinkNotificationPreferencesQuery request, CancellationToken ct)
    {
        var scope = await _access.ResolveLinkForOwnerRightsAsync(request.OwnerAccountId, request.LinkId, ct);
        if (!scope.IsSuccess)
            return Result<PortalLinkNotificationPreferencesDto>.Failure(scope.Error);

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(scope.Value!.TenantId, scope.Value.ClientId), ct);
        var link = await _links.FirstOrDefaultAsync(new OwnerAccountClientLinkByIdSpec(scope.Value.LinkId), ct);
        return client is null || link is null
            ? Result<PortalLinkNotificationPreferencesDto>.Failure("Portal.Links.NotFound", "Kayıt bulunamadı.")
            : Result<PortalLinkNotificationPreferencesDto>.Success(PortalNotificationPreferencesMapping.ToDto(client, link));
    }
}

public sealed class UpdatePortalLinkNotificationPreferencesCommandHandler
    : IRequestHandler<UpdatePortalLinkNotificationPreferencesCommand, Result<PortalLinkNotificationPreferencesDto>>
{
    private readonly OwnerPortalAccess _access;
    private readonly IRepository<Client> _clients;
    private readonly IRepository<OwnerAccountClientLink> _links;
    private readonly TimeProvider _timeProvider;

    public UpdatePortalLinkNotificationPreferencesCommandHandler(
        OwnerPortalAccess access,
        IRepository<Client> clients,
        IRepository<OwnerAccountClientLink> links,
        TimeProvider timeProvider)
    {
        _access = access;
        _clients = clients;
        _links = links;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<PortalLinkNotificationPreferencesDto>> Handle(UpdatePortalLinkNotificationPreferencesCommand request, CancellationToken ct)
    {
        var scope = await _access.ResolveLinkForOwnerRightsAsync(request.OwnerAccountId, request.LinkId, ct);
        if (!scope.IsSuccess)
            return Result<PortalLinkNotificationPreferencesDto>.Failure(scope.Error);

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(scope.Value!.TenantId, scope.Value.ClientId), ct);
        var link = await _links.FirstOrDefaultAsync(new OwnerAccountClientLinkByIdSpec(scope.Value.LinkId), ct);
        if (client is null || link is null)
            return Result<PortalLinkNotificationPreferencesDto>.Failure("Portal.Links.NotFound", "Kayıt bulunamadı.");

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        if (request.RemindersOptedOut is { } optedOut)
        {
            // Kural tek yerde (APPT-014): KVKK Ret kaydı varken açma reddedilir.
            var applied = client.SetRemindersOptedOutByOwner(optedOut, utcNow);
            if (!applied.IsSuccess)
                return Result<PortalLinkNotificationPreferencesDto>.Failure(applied.Error);
        }

        if (request.MarketingConsented is { } consented)
            link.SetMarketingConsent(consented, utcNow);

        try
        {
            await _clients.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<PortalLinkNotificationPreferencesDto>.Failure(
                "Clients.ConcurrencyConflict",
                "Kayıt bu sırada klinik tarafından güncellendi; sayfayı yenileyip tekrar deneyin.");
        }

        return Result<PortalLinkNotificationPreferencesDto>.Success(PortalNotificationPreferencesMapping.ToDto(client, link));
    }
}
