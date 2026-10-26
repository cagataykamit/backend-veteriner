using Mediator;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Common.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, IMessage
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger) => _logger = logger;

    public async ValueTask<TResponse> Handle(TRequest request, MessageHandlerDelegate<TRequest, TResponse> next, CancellationToken ct)
    {
        _logger.LogInformation("Handling {RequestName}", typeof(TRequest).Name);
        try
        {
            var response = await next(request, ct);
            _logger.LogInformation("Handled {RequestName}", typeof(TRequest).Name);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in {RequestName}", typeof(TRequest).Name);
            throw;
        }
    }
}
