using System.Diagnostics;
using Backend.Veteriner.Application.Common.Constants;
using Serilog.Context;

namespace Backend.Veteriner.Api.Middleware;

public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext context)
    {
        var headerName = Correlation.HeaderName; // "X-Correlation-ID"

        // Güvenli değerse kullan, değilse üret
        var cid = Correlation.Normalize(context.Request.Headers[headerName].ToString());

        // Request ömrü boyunca erişilebilir olsun
        context.Items[headerName] = cid;

        // Response header'a yaz
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[headerName] = cid;
            return Task.CompletedTask;
        });

        Activity.Current?.SetTag("correlation.id", cid);

        // Serilog'a property olarak bas
        using (LogContext.PushProperty("CorrelationId", cid))
        using (LogContext.PushProperty("TraceId", Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier))
        {
            await _next(context);
        }
    }
}
