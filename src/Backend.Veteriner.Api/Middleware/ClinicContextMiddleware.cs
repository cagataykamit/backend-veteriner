using System.Diagnostics;
using System.Security.Claims;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Application.Common.Clinic;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Middleware;

/// <summary>
/// JWT <c>clinic_id</c> ile header/query <c>clinicId</c> çakışmasını engeller; çözümlenmiş kliniği Items'a yazar.
/// Authentication sonrası çalışmalıdır.
/// </summary>
public sealed class ClinicContextMiddleware
{
    private readonly RequestDelegate _next;

    public ClinicContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var headerRaw = context.Request.Headers[ClinicRequestResolver.HeaderName].FirstOrDefault();
        var queryRaw = context.Request.Query["clinicId"].FirstOrDefault();

        var claims = context.User?.Claims ?? Enumerable.Empty<Claim>();
        var result = ClinicRequestResolver.Resolve(claims, headerRaw, queryRaw);

        if (result.ClinicConflict)
        {
            await WriteClinicConflictProblemAsync(context);
            return;
        }

        if (result.ClinicId.HasValue)
            context.Items[ClinicHttpContextKeys.ResolvedClinicId] = result.ClinicId.Value;
        else
            context.Items.Remove(ClinicHttpContextKeys.ResolvedClinicId);

        await _next(context);
    }

    private static async Task WriteClinicConflictProblemAsync(HttpContext context)
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
            Title = "Clinic uyuşmazlığı",
            Type = "https://httpstatuses.io/403",
            Detail = "JWT clinic_id ile istek clinicId farklı; güvenlik için istek reddedildi.",
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = traceId;
        problem.Extensions["correlationId"] = correlationId;
        problem.Extensions["code"] = "Context.ClinicConflict";
        problem.Extensions["timestampUtc"] = DateTime.UtcNow;

        await context.Response.WriteAsJsonAsync(problem);
    }
}

