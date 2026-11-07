using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Users.Common;
using Backend.Veteriner.Domain.ExternalApi;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Users;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.ExternalApi;

/// <summary>Panel anahtar listesi satırı; gizli kısım ve özet hiçbir yanıtta yoktur. Ad <c>DisplayName</c> yoksa e-postadan türetilir.</summary>
public sealed record ApiKeyDto(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    Guid CreatedByUserId,
    string? CreatedByName,
    DateTime CreatedAtUtc,
    DateTime? LastUsedAtUtc,
    bool IsRevoked,
    DateTime? RevokedAtUtc,
    string? RevokedByName);

public sealed record ApiKeyListDto(IReadOnlyList<ApiKeyDto> Items, IReadOnlyList<string> AvailableScopes, int MaxActiveKeys);

/// <summary>Oluşturma yanıtı: <see cref="PlainTextKey"/> yalnız burada ve bir kez döner; sunucu onu bir daha üretemez.</summary>
public sealed record CreatedApiKeyDto(ApiKeyDto Key, string PlainTextKey);

public sealed record GetApiKeysQuery : IRequest<Result<ApiKeyListDto>>;

public sealed record CreateApiKeyCommand(string? Name, IReadOnlyList<string>? Scopes)
    : IRequest<Result<CreatedApiKeyDto>>, IAuditableRequest
{
    public string AuditAction => "ApiKey.Create";
    public string? AuditTarget => $"Name={Name?.Trim()};Scopes={string.Join(ApiKeyScopes.Separator, Scopes ?? [])}";
}

/// <summary>İptal güvenlik işlemidir: salt okunur ya da düşmüş pakette de yapılabilir.</summary>
public sealed record RevokeApiKeyCommand(Guid Id) : IRequest<Result>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "ApiKey.Revoke";
    public string? AuditTarget => $"ApiKeyId={Id}";
}

public sealed class CreateApiKeyCommandValidator : AbstractValidator<CreateApiKeyCommand>
{
    public CreateApiKeyCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ApiKey.MaxNameLength);
        RuleFor(x => x.Scopes).NotEmpty();
    }
}

public sealed class ApiKeysByTenantSpec : Specification<ApiKey>
{
    public ApiKeysByTenantSpec(Guid tenantId)
    {
        Query.AsNoTracking();
        Query.Where(k => k.TenantId == tenantId).OrderBy(k => k.RevokedAtUtc.HasValue).ThenByDescending(k => k.CreatedAtUtc);
    }
}

public sealed class ActiveApiKeysByTenantSpec : Specification<ApiKey>
{
    public ActiveApiKeysByTenantSpec(Guid tenantId) => Query.Where(k => k.TenantId == tenantId && k.RevokedAtUtc == null);
}

public sealed class ApiKeyForUpdateSpec : Specification<ApiKey>, ISingleResultSpecification<ApiKey>
{
    public ApiKeyForUpdateSpec(Guid tenantId, Guid id) => Query.Where(k => k.TenantId == tenantId && k.Id == id);
}

public sealed record ApiKeyUser(Guid Id, string? DisplayName, string Email);

public sealed class ApiKeyUsersSpec : Specification<User, ApiKeyUser>
{
    public ApiKeyUsersSpec(IReadOnlyCollection<Guid> userIds)
    {
        Query.AsNoTracking();
        Query.Where(u => userIds.Contains(u.Id));
        Query.Select(u => new ApiKeyUser(u.Id, u.DisplayName, u.Email));
    }
}

internal static class ApiKeyRules
{
    public static Result<(Guid TenantId, Guid UserId)> ResolveActor(ITenantContext tenantContext, IClientContext clientContext)
    {
        if (tenantContext.TenantId is not { } tenantId)
            return Result<(Guid, Guid)>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");
        if (clientContext.UserId is not { } userId)
            return Result<(Guid, Guid)>.Failure("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");
        return Result<(Guid, Guid)>.Success((tenantId, userId));
    }

    public static async Task<IReadOnlyList<ApiKeyDto>> ToDtosAsync(IReadRepository<User> users, IReadOnlyList<ApiKey> keys, CancellationToken ct)
    {
        var ids = keys.Select(k => k.CreatedByUserId)
            .Concat(keys.Where(k => k.RevokedByUserId.HasValue).Select(k => k.RevokedByUserId!.Value))
            .Distinct()
            .ToArray();
        var names = ids.Length == 0
            ? new Dictionary<Guid, string?>()
            : (await users.ListAsync(new ApiKeyUsersSpec(ids), ct)).ToDictionary(u => u.Id, u => UserDisplayName.Resolve(u.DisplayName, u.Email));

        return keys.Select(k => new ApiKeyDto(
                k.Id, k.Name, k.Prefix, k.ScopeList, k.CreatedByUserId, names.GetValueOrDefault(k.CreatedByUserId), k.CreatedAtUtc,
                k.LastUsedAtUtc, k.IsRevoked, k.RevokedAtUtc, k.RevokedByUserId is { } r ? names.GetValueOrDefault(r) : null))
            .ToList();
    }
}

public sealed class GetApiKeysQueryHandler : IRequestHandler<GetApiKeysQuery, Result<ApiKeyListDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<ApiKey> _keys;
    private readonly IReadRepository<User> _users;

    public GetApiKeysQueryHandler(ITenantContext tenantContext, IReadRepository<ApiKey> keys, IReadRepository<User> users)
    {
        _tenantContext = tenantContext;
        _keys = keys;
        _users = users;
    }

    public async ValueTask<Result<ApiKeyListDto>> Handle(GetApiKeysQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<ApiKeyListDto>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");

        var keys = await _keys.ListAsync(new ApiKeysByTenantSpec(tenantId), ct);
        return Result<ApiKeyListDto>.Success(
            new ApiKeyListDto(await ApiKeyRules.ToDtosAsync(_users, keys, ct), ApiKeyScopes.All, ApiKey.MaxActivePerTenant));
    }
}

public sealed class CreateApiKeyCommandHandler : IRequestHandler<CreateApiKeyCommand, Result<CreatedApiKeyDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly IRepository<ApiKey> _keys;
    private readonly IReadRepository<User> _users;
    private readonly TimeProvider _timeProvider;

    public CreateApiKeyCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        TenantPlanFeatureEvaluator planFeatures,
        IRepository<ApiKey> keys,
        IReadRepository<User> users,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _planFeatures = planFeatures;
        _keys = keys;
        _users = users;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<CreatedApiKeyDto>> Handle(CreateApiKeyCommand request, CancellationToken ct)
    {
        var actor = ApiKeyRules.ResolveActor(_tenantContext, _clientContext);
        if (!actor.IsSuccess)
            return Result<CreatedApiKeyDto>.Failure(actor.Error);
        var (tenantId, userId) = actor.Value;

        var planAllows = await _planFeatures.EnsureAllowedAsync(tenantId, PlanFeature.ExternalApi, ct);
        if (!planAllows.IsSuccess)
            return Result<CreatedApiKeyDto>.Failure(planAllows.Error);

        if (await _keys.CountAsync(new ActiveApiKeysByTenantSpec(tenantId), ct) >= ApiKey.MaxActivePerTenant)
        {
            return Result<CreatedApiKeyDto>.Failure(
                "ApiKeys.LimitReached",
                $"En fazla {ApiKey.MaxActivePerTenant} etkin anahtar olabilir; kullanılmayan bir anahtarı iptal edin.");
        }

        var secret = ApiKeySecret.Generate();
        var created = ApiKey.Create(
            tenantId, request.Name, request.Scopes, secret.Prefix, secret.Hash, userId, _timeProvider.GetUtcNow().UtcDateTime);
        if (!created.IsSuccess)
            return Result<CreatedApiKeyDto>.Failure(created.Error);

        await _keys.AddAsync(created.Value!, ct);
        await _keys.SaveChangesAsync(ct);

        var dto = (await ApiKeyRules.ToDtosAsync(_users, [created.Value!], ct))[0];
        return Result<CreatedApiKeyDto>.Success(new CreatedApiKeyDto(dto, secret.PlainText));
    }
}

public sealed class RevokeApiKeyCommandHandler : IRequestHandler<RevokeApiKeyCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IRepository<ApiKey> _keys;
    private readonly TimeProvider _timeProvider;

    public RevokeApiKeyCommandHandler(
        ITenantContext tenantContext, IClientContext clientContext, IRepository<ApiKey> keys, TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _keys = keys;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(RevokeApiKeyCommand request, CancellationToken ct)
    {
        var actor = ApiKeyRules.ResolveActor(_tenantContext, _clientContext);
        if (!actor.IsSuccess)
            return Result.Failure(actor.Error);
        var (tenantId, userId) = actor.Value;

        var key = await _keys.FirstOrDefaultAsync(new ApiKeyForUpdateSpec(tenantId, request.Id), ct);
        if (key is null)
            return Result.Failure("ApiKeys.NotFound", "API anahtarı bulunamadı.");

        var revoked = key.Revoke(userId, _timeProvider.GetUtcNow().UtcDateTime);
        if (!revoked.IsSuccess)
            return revoked;

        await _keys.SaveChangesAsync(ct);
        return Result.Success();
    }
}
