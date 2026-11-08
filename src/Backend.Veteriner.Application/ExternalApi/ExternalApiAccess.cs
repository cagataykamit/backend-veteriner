using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.ExternalApi;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.ExternalApi;

/// <summary>Doğrulanmış anahtarın sahibi: kiracı yalnız anahtardan gelir, istekte kiracı parametresi yoktur.</summary>
public sealed record ExternalApiCaller(Guid KeyId, Guid TenantId, IReadOnlyList<string> Scopes);

public static class ExternalApiErrors
{
    public const string KeyMissingCode = "ExternalApi.KeyMissing";
    public const string KeyInvalidCode = "ExternalApi.KeyInvalid";
    public const string KeyRevokedCode = "ExternalApi.KeyRevoked";
    public const string BearerNotAcceptedCode = "ExternalApi.BearerNotAccepted";
    public const string ScopeMissingCode = "ExternalApi.ScopeMissing";
    public const string ValidationCode = "ExternalApi.Validation";

    public static readonly Error KeyMissing = new(KeyMissingCode, $"API anahtarı gerekli ({ExternalApiAuth.HeaderName} başlığı).");
    public static readonly Error KeyInvalid = new(KeyInvalidCode, "API anahtarı geçersiz.");
    public static readonly Error KeyRevoked = new(KeyRevokedCode, "API anahtarı iptal edilmiş.");
    public static readonly Error BearerNotAccepted = new(
        BearerNotAcceptedCode, $"Dış API panel oturumu (Authorization başlığı) kabul etmez; yalnız {ExternalApiAuth.HeaderName} kullanılır.");
    public static readonly Error ScopeMissing = new(ScopeMissingCode, "API anahtarının bu uç için kapsamı yok.");
}

public sealed class ApiKeyByPrefixSpec : Specification<ApiKey>, ISingleResultSpecification<ApiKey>
{
    public ApiKeyByPrefixSpec(string prefix) => Query.Where(k => k.Prefix == prefix);
}

/// <summary>
/// Dış API erişim kuralı — tek yer (TIER-009; docs/EXTERNAL_API_CONTRACT.md §2–§3). Anahtar ön ekle kiracılar arası bulunur (sistem
/// kapsamı), özet sabit zamanlı karşılaştırılır; geçerliyse isteğin geri kalanı anahtarın kiracısına bağlanır ve ADR-040 kiracı
/// filtresi bu kiracıyla çalışır. Paket ve abonelik kapısı kimlikten ayrıdır: kimlik 401, paket ve abonelik 403.
/// </summary>
public sealed class ExternalApiAccess
{
    private readonly IRepository<ApiKey> _keys;
    private readonly ITenantScope _tenantScope;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly TenantSubscriptionEffectiveWriteEvaluator _subscriptions;
    private readonly TimeProvider _timeProvider;

    public ExternalApiAccess(
        IRepository<ApiKey> keys,
        ITenantScope tenantScope,
        TenantPlanFeatureEvaluator planFeatures,
        TenantSubscriptionEffectiveWriteEvaluator subscriptions,
        TimeProvider timeProvider)
    {
        _keys = keys;
        _tenantScope = tenantScope;
        _planFeatures = planFeatures;
        _subscriptions = subscriptions;
        _timeProvider = timeProvider;
    }

    public async Task<Result<ExternalApiCaller>> AuthenticateAsync(string? presentedKey, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(presentedKey))
            return Result<ExternalApiCaller>.Failure(ExternalApiErrors.KeyMissing);
        if (!ApiKeySecret.TryGetPrefix(presentedKey, out var prefix))
            return Result<ExternalApiCaller>.Failure(ExternalApiErrors.KeyInvalid);

        ApiKey? key;
        using (_tenantScope.AsSystem())
            key = await _keys.FirstOrDefaultAsync(new ApiKeyByPrefixSpec(prefix), ct);

        // Ön ek bulunmasa da özet hesaplanıp karşılaştırılır; yanıt yalnız tam eşleşmede değişir.
        if (!ApiKeySecret.Verify(presentedKey, key?.SecretHash) || key is null)
            return Result<ExternalApiCaller>.Failure(ExternalApiErrors.KeyInvalid);
        if (key.IsRevoked)
            return Result<ExternalApiCaller>.Failure(ExternalApiErrors.KeyRevoked);

        // İsteğin geri kalanı anahtarın kiracısına bağlanır; kapsam DI kapsamıyla (istekle) biter.
        _ = _tenantScope.UseTenant(key.TenantId);

        if (key.MarkUsed(_timeProvider.GetUtcNow().UtcDateTime))
            await _keys.SaveChangesAsync(ct);

        return Result<ExternalApiCaller>.Success(new ExternalApiCaller(key.Id, key.TenantId, key.ScopeList));
    }

    /// <summary>
    /// Paket Kurumsal değilse 403 <c>Subscriptions.FeatureNotInPlan</c>. Pasif kiracıda, süresi dolmuş denemede, salt okunur ya da iptal
    /// edilmiş abonelikte de kapalıdır (panel okuması bu kuraldan bağımsız açık kalır).
    /// </summary>
    public async Task<Result> EnsureTenantAllowedAsync(Guid tenantId, CancellationToken ct)
    {
        var plan = await _planFeatures.EnsureAllowedAsync(tenantId, PlanFeature.ExternalApi, ct);
        if (!plan.IsSuccess)
            return plan;

        return await _subscriptions.EnsureWriteAllowedAsync(tenantId, ct);
    }
}
