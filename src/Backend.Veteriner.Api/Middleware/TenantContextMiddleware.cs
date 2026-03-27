using System.Diagnostics;
using System.Security.Claims;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Application.Common.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Middleware;

/// <summary>
/// JWT <c>tenant_id</c> ile sorgu <c>tenantId</c> çakışmasını engeller; çözümlenmiş kiracıyı Items'a yazar.
/// <see cref="Microsoft.AspNetCore.Builder.WebApplication"/> pipeline'ında Authentication sonrası çalışmalıdır.
/// </summary>
public sealed class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    public TenantContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var queryRaw = context.Request.Query["tenantId"].FirstOrDefault();
        var claims = context.User?.Claims ?? Enumerable.Empty<Claim>();
        var result = TenantRequestResolver.Resolve(claims, queryRaw);

        if (result.TenantConflict)
        {
            await WriteTenantConflictProblemAsync(context);
            return;
        }

        if (result.TenantId.HasValue)
            context.Items[TenantHttpContextKeys.ResolvedTenantId] = result.TenantId.Value;
        else
            context.Items.Remove(TenantHttpContextKeys.ResolvedTenantId);

        await _next(context);
    }

    private static async Task WriteTenantConflictProblemAsync(HttpContext context)
    {
        if (context.Response.HasStarted)
            return;

        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        var correlationId =
            (context.Items.TryGetValue(Correlation.HeaderName, out var v) ? v?.ToString() : null)
            ?? (context.Request.Headers.TryGetValue(Correlation.HeaderName, out var cid) ? cid.ToString() : null)
            ?? traceId;
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Tenant uyuşmazlığı",
            Type = "https://httpstatuses.io/403",
            Detail = "JWT tenant_id ile sorgu tenantId farklı; güvenlik için istek reddedildi.",
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = traceId;
        problem.Extensions["correlationId"] = correlationId;
        problem.Extensions["code"] = "Context.TenantConflict";
        problem.Extensions["timestampUtc"] = DateTime.UtcNow;

        await context.Response.WriteAsJsonAsync(problem);
    }
}
