using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Auditing;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.OwnerPortal.MarketingOptOut;

/// <summary>Ret sayfası: asgari veri — klinik adı ve sahibin bu klinik için onayının sürüp sürmediği.</summary>
public sealed record MarketingOptOutPageDto(string ClinicName, bool IsOptedOut);

/// <summary>Ret sayfası bilgisi (kimliksiz; ADR-046 K3). Durumu değiştirmez.</summary>
public sealed record GetMarketingOptOutQuery(string Token) : IRequest<Result<MarketingOptOutPageDto>>;

/// <summary>
/// Ticari iletiyi reddeder: bağdaki onay geri alınır ve ret zamanı yazılır (ADR-046 K4). Tekrar çağrı başarılıdır, durumu değiştirmez.
/// Kiracı salt okunur olsa da çalışır.
/// </summary>
public sealed record ConfirmMarketingOptOutCommand(string Token) : IRequest<Result>, IIgnoreTenantWriteSubscriptionGuard;

public sealed class GetMarketingOptOutQueryHandler : IRequestHandler<GetMarketingOptOutQuery, Result<MarketingOptOutPageDto>>
{
    private readonly MarketingOptOutLinkResolver _resolver;

    public GetMarketingOptOutQueryHandler(MarketingOptOutLinkResolver resolver) => _resolver = resolver;

    public async ValueTask<Result<MarketingOptOutPageDto>> Handle(GetMarketingOptOutQuery request, CancellationToken ct)
    {
        var scope = await _resolver.ResolveAsync(request.Token, ct);
        return scope.IsSuccess
            ? Result<MarketingOptOutPageDto>.Success(new MarketingOptOutPageDto(scope.Value!.ClinicName, !scope.Value.ClientLink.MarketingConsentAtUtc.HasValue))
            : Result<MarketingOptOutPageDto>.Failure(scope.Error);
    }
}

/// <summary>Denetimde aktör "hasta sahibi (ret bağlantısı)"dır: kullanıcı yoktur, token denetime yazılmaz (TIER-019 ile aynı kural).</summary>
public sealed class ConfirmMarketingOptOutCommandHandler : IRequestHandler<ConfirmMarketingOptOutCommand, Result>
{
    public const string AuditAction = "Portal.MarketingOptOut";
    public const string AuditActorPayload = "{\"actorKind\":\"MarketingOptOutLink\"}";

    private readonly MarketingOptOutLinkResolver _resolver;
    private readonly IRepository<MarketingOptOutLink> _optOutLinks;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IClientContext _client;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ConfirmMarketingOptOutCommandHandler> _logger;

    public ConfirmMarketingOptOutCommandHandler(
        MarketingOptOutLinkResolver resolver,
        IRepository<MarketingOptOutLink> optOutLinks,
        IAuditLogWriter auditLogWriter,
        IClientContext client,
        TimeProvider timeProvider,
        ILogger<ConfirmMarketingOptOutCommandHandler> logger)
    {
        _resolver = resolver;
        _optOutLinks = optOutLinks;
        _auditLogWriter = auditLogWriter;
        _client = client;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async ValueTask<Result> Handle(ConfirmMarketingOptOutCommand request, CancellationToken ct)
    {
        var scope = await _resolver.ResolveAsync(request.Token, ct);
        if (!scope.IsSuccess)
            return await AuditAsync(null, Result.Failure(scope.Error), ct);

        var s = scope.Value!;
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        s.ClientLink.SetMarketingConsent(false, utcNow);
        s.OptOutLink.MarkUsed(utcNow);
        await _optOutLinks.SaveChangesAsync(ct);
        return await AuditAsync(s.ClientLink.Id, Result.Success(), ct);
    }

    /// <summary>Denetim yazımı iş sonucunu etkilemez (<c>AuditBehavior</c> ile aynı kural).</summary>
    private async Task<Result> AuditAsync(Guid? clientLinkId, Result result, CancellationToken ct)
    {
        var entry = new AuditLogEntry(
            ActorUserId: null,
            Action: AuditAction,
            TargetType: nameof(OwnerAccountClientLink),
            TargetId: clientLinkId?.ToString("D"),
            Success: result.IsSuccess,
            FailureReason: result.IsSuccess ? null : result.Error.Code,
            Route: _client.Path,
            HttpMethod: _client.Method,
            IpAddress: _client.IpAddress,
            UserAgent: _client.UserAgent,
            CorrelationId: _client.CorrelationId,
            RequestName: nameof(ConfirmMarketingOptOutCommand),
            RequestPayload: AuditActorPayload,
            OccurredAtUtc: _timeProvider.GetUtcNow().UtcDateTime);

        try
        {
            await _auditLogWriter.WriteAsync(entry, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Denetim kaydı yazılamadı. Action={Action} CorrelationId={CorrelationId}", entry.Action, entry.CorrelationId);
        }

        return result;
    }
}
