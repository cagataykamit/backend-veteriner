using Backend.Veteriner.Application.Common.Diagnostics;
using Backend.Veteriner.Application.Common.Options;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Backend.Veteriner.Application.Common.Behaviors;

public sealed class PerformanceBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, IMessage
{
    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger;
    private readonly PerformanceDiagnosticsOptions _options;

    public PerformanceBehavior(
        ILogger<PerformanceBehavior<TRequest, TResponse>> logger,
        IOptions<PerformanceDiagnosticsOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async ValueTask<TResponse> Handle(TRequest request, MessageHandlerDelegate<TRequest, TResponse> next, CancellationToken ct)
    {
        // Süre yerel tutulur: Scoped yaşam süresinde aynı örnek kapsam içindeki tekrar eden isteklerde paylaşılır.
        var startedAt = Stopwatch.GetTimestamp();
        var response = await next(request, ct);
        var elapsed = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

        if (!_options.Enabled)
            return response;

        if (elapsed < _options.MediatorSlowMs)
            return response;

        _logger.LogWarning(
            "Mediator.Handler.Slow {RequestName} ElapsedMs={ElapsedMs} ThresholdMs={ThresholdMs} ActiveHttpRequestsApprox={ActiveHttpRequestsApprox}",
            typeof(TRequest).Name,
            elapsed,
            _options.MediatorSlowMs,
            ActiveHttpRequestCounter.ApproximateActive);

        return response;
    }
}
