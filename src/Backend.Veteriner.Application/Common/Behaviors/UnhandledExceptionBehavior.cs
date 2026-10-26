using Mediator;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Common.Behaviors;

/// <summary>
/// En dış ara katman: iç katmanlardan kaçan istisnayı tek yerde loglar ve aynen iletir.
/// İstemcinin bıraktığı istek (iptal) hata değildir: Information düzeyinde geçer, 499'u middleware verir.
/// SqlClient iptali <c>OperationCanceledException</c> yerine sağlayıcı istisnası olarak da fırlatabildiği için
/// tür değil, iptal edilmiş token belirleyicidir.
/// </summary>
public sealed class UnhandledExceptionBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, IMessage
{
    private readonly ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> _logger;
    public UnhandledExceptionBehavior(ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> logger) => _logger = logger;

    public async ValueTask<TResponse> Handle(TRequest request, MessageHandlerDelegate<TRequest, TResponse> next, CancellationToken ct)
    {
        try
        {
            return await next(request, ct);
        }
        catch (Exception ex) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Request cancelled by client for {RequestName} ({ExceptionType})", typeof(TRequest).Name, ex.GetType().Name);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {RequestName}", typeof(TRequest).Name);
            throw;
        }
    }
}
