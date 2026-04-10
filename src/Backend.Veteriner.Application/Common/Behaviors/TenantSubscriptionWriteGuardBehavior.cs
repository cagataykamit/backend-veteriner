using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Common.Behaviors;

/// <summary>
/// Tenant subscription / trial durumuna göre mutation (Command) isteklerini merkezi olarak engeller.
/// - Okuma (Query) isteklerine dokunmaz.
/// - Auth akışlarını (login/refresh/select-clinic vb.) hariç tutar.
/// </summary>
public sealed class TenantSubscriptionWriteGuardBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ITenantContext _tenantContext;
    private readonly TenantSubscriptionEffectiveWriteEvaluator _evaluator;

    public TenantSubscriptionWriteGuardBehavior(
        ITenantContext tenantContext,
        TenantSubscriptionEffectiveWriteEvaluator evaluator)
    {
        _tenantContext = tenantContext;
        _evaluator = evaluator;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is IIgnoreTenantWriteSubscriptionGuard)
            return await next();

        if (!ShouldEnforceForRequestType(typeof(TRequest)))
            return await next();

        var tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            // Tenant context çözümlenmemişse davranışı bozmayalım:
            // - Public owner signup gibi tenant oluşmadan çalışan use-case'ler etkilenmez.
            // - Tenant ile ilişkili write işlemleri zaten handler seviyesinde tenantId doğrulaması yapıyor.
            return await next();
        }

        var gate = await _evaluator.EnsureWriteAllowedAsync(tenantId.Value, ct);
        if (gate.IsSuccess)
            return await next();

        return Fail<TResponse>(gate.Error);
    }

    private static bool ShouldEnforceForRequestType(Type requestType)
    {
        // MediatR convention: mutation = *Command, read = *Query
        if (!requestType.Name.EndsWith("Command", StringComparison.Ordinal))
            return false;

        // Ürün kararı: login/refresh/select-clinic açık kalmalı.
        // Basit ve güvenli: Auth namespace'indeki command'ları guard dışında tut.
        var ns = requestType.Namespace ?? "";
        if (ns.Contains(".Application.Auth.", StringComparison.Ordinal))
            return false;

        return true;
    }

    private static T Fail<T>(Error error)
    {
        if (typeof(T) == typeof(Result))
        {
            var r = Result.Failure(error);
            return (T)(object)r;
        }

        if (typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var genericArg = typeof(T).GetGenericArguments()[0];
            var method = typeof(TenantSubscriptionWriteGuardBehavior<TRequest, TResponse>)
                .GetMethod(nameof(CreateGenericFailure), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(genericArg);
            return (T)method.Invoke(null, new object[] { error })!;
        }

        throw new InvalidOperationException(
            $"TenantSubscriptionWriteGuardBehavior only supports Result/Result<T> responses. Response was: {typeof(T).FullName}");
    }

    private static Result<TValue> CreateGenericFailure<TValue>(Error error)
        => Result<TValue>.Failure(error);
}

