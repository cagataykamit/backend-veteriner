using Backend.Veteriner.Application.Common.Constants; // ?? sabit burada
using Serilog.Context;

namespace Backend.Veteriner.Api.Middleware;

public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext context)
    {
        var headerName = Correlation.HeaderName; // "X-Correlation-ID"

        // Var ise kullan, yoksa �ret
        var cid = context.Request.Headers.TryGetValue(headerName, out var values) && !string.IsNullOrWhiteSpace(values)
            ? values.ToString()
            : Guid.NewGuid().ToString("N");

        // Request �mr� boyunca eri�ilebilir olsun
        context.Items[headerName] = cid;

        // Response header�a yaz
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[headerName] = cid;
            return Task.CompletedTask;
        });

        // Serilog�a property olarak bas
        using (LogContext.PushProperty("CorrelationId", cid))
        {
            await _next(context);
        }
    }
}
