using Backend.Veteriner.Application.Common.Diagnostics;

namespace Backend.Veteriner.Api.Middleware;

/// <summary>
/// Eşzamanlı API istek sayısını artırır (EF tanılama logları ile stall / pool korelasyonu için).
/// </summary>
public sealed class ActiveHttpRequestCountingMiddleware
{
    private readonly RequestDelegate _next;

    public ActiveHttpRequestCountingMiddleware(RequestDelegate next)
        => _next = next;

    public async Task Invoke(HttpContext context)
    {
        var path = context.Request.Path;
        if (ShouldSkip(path))
        {
            await _next(context);
            return;
        }

        ActiveHttpRequestCounter.EnterHttpRequest();
        try
        {
            await _next(context);
        }
        finally
        {
            ActiveHttpRequestCounter.LeaveHttpRequest();
        }
    }

    private static bool ShouldSkip(PathString path)
        => path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
           || path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase);
}
