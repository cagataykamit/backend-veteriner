using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Backend.Veteriner.Application.Common.Behaviors;

public sealed class PerformanceBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger;
    private readonly Stopwatch _sw = new();

    public PerformanceBehavior(ILogger<PerformanceBehavior<TRequest, TResponse>> logger) => _logger = logger;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        _sw.Restart();
        var response = await next();
        _sw.Stop();

        if (_sw.ElapsedMilliseconds > 250)
            _logger.LogWarning("{RequestName} took {Elapsed} ms", typeof(TRequest).Name, _sw.ElapsedMilliseconds);

        return response;
    }
}
