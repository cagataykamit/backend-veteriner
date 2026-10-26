using Mediator;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Common.Behaviors;

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {RequestName}", typeof(TRequest).Name);
            throw;
        }
    }
}
