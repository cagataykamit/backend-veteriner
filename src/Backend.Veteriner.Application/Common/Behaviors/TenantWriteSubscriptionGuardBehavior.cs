using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Common.Behaviors;

/// <summary>
/// JWT <see cref="ITenantContext.TenantId"/> varken, kiracıya bağlı <c>Result</c>/<c>Result&lt;T&gt;</c> dönen command'lerde
/// abonelik + trial bitişine göre yazmayı merkezi keser. Sorgular (<c>*Query</c>) ve <see cref="IIgnoreTenantWriteSubscriptionGuard"/> hariçtir.
/// </summary>
public sealed class TenantWriteSubscriptionGuardBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ITenantContext _tenantContext;
    private readonly ITenantSubscriptionWriteGuard _writeGuard;

    public TenantWriteSubscriptionGuardBehavior(
        ITenantContext tenantContext,
        ITenantSubscriptionWriteGuard writeGuard)
    {
        _tenantContext = tenantContext;
        _writeGuard = writeGuard;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is IIgnoreTenantWriteSubscriptionGuard)
            return await next();

        if (typeof(TRequest).Name.EndsWith("Query", StringComparison.Ordinal))
            return await next();

        if (!IsResultContract(typeof(TResponse)))
            return await next();

        if (_tenantContext.TenantId is not { } tenantId)
            return await next();

        var writeResult = await _writeGuard.EnsureWritesAllowedAsync(tenantId, cancellationToken);
        if (!writeResult.IsSuccess)
            return MapFailure(writeResult.Error);

        return await next();
    }

    private static bool IsResultContract(Type t)
    {
        if (t == typeof(Result))
            return true;
        return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Result<>);
    }

    private static TResponse MapFailure(Error error)
    {
        var t = typeof(TResponse);
        if (t == typeof(Result))
            return (TResponse)(object)Result.Failure(error);

        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var closed = typeof(Result<>).MakeGenericType(t.GetGenericArguments()[0]);
            var failure = closed.GetMethod("Failure", new[] { typeof(Error) });
            if (failure is null)
                throw new InvalidOperationException("Result<T>.Failure(Error) yok.");
            return (TResponse)failure.Invoke(null, new object[] { error })!;
        }

        throw new InvalidOperationException($"Beklenmeyen yanıt tipi: {t.Name}");
    }
}
