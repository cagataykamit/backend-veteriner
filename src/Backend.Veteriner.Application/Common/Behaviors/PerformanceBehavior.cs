using Backend.Veteriner.Application.Common.Diagnostics;
using Backend.Veteriner.Application.Common.Options;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Backend.Veteriner.Application.Common.Behaviors;

public sealed class PerformanceBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger;
    private readonly PerformanceDiagnosticsOptions _options;
    private readonly Stopwatch _sw = new();

    public PerformanceBehavior(
        ILogger<PerformanceBehavior<TRequest, TResponse>> logger,
        IOptions<PerformanceDiagnosticsOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        _sw.Restart();
        var response = await next();
        _sw.Stop();

        if (!_options.Enabled)
            return response;

        var elapsed = _sw.ElapsedMilliseconds;
        if (elapsed < _options.MediatRSlowMs)
            return response;

        _logger.LogWarning(
            "MediatR.Handler.Slow {RequestName} ElapsedMs={ElapsedMs} ThresholdMs={ThresholdMs} ActiveHttpRequestsApprox={ActiveHttpRequestsApprox}",
            typeof(TRequest).Name,
            elapsed,
            _options.MediatRSlowMs,
            ActiveHttpRequestCounter.ApproximateActive);

        return response;
    }
}
