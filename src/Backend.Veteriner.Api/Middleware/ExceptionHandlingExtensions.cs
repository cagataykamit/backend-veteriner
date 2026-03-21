using System.Diagnostics;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace Backend.Veteriner.Api.Middleware;

public static class ExceptionHandlingExtensions
{
    private const int ClientClosedRequest = 499;

    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async ctx =>
            {
                if (ctx.Response.HasStarted) return;

                var feature = ctx.Features.Get<IExceptionHandlerFeature>();
                var ex = feature?.Error;

                var traceId = Activity.Current?.Id ?? ctx.TraceIdentifier;

                var correlationId =
                    ctx.Items.TryGetValue("CorrelationId", out var v) ? v?.ToString() :
                    ctx.Request.Headers.TryGetValue("X-Correlation-Id", out var cid) ? cid.ToString() :
                    traceId;

                if (ex is not null)
                    Log.Error(ex, "Unhandled exception. TraceId={TraceId} CorrelationId={CorrelationId}", traceId, correlationId);
                else
                    Log.Error("Unhandled error without exception. TraceId={TraceId} CorrelationId={CorrelationId}", traceId, correlationId);

                ctx.Response.ContentType = "application/problem+json";

                // 0) Exception null ise bile s�zle?me bozulmas?n
                if (ex is null)
                {
                    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;

                    var pdNull = CreateProblemDetails(
                        status: StatusCodes.Status500InternalServerError,
                        title: "Beklenmeyen hata",
                        type: "https://httpstatuses.io/500",
                        detail: env.IsDevelopment()
                            ? "Exception al?namad? (null)."
                            : "Beklenmeyen bir hata olu?tu.",
                        instance: ctx.Request.Path,
                        traceId: traceId,
                        correlationId: correlationId,
                        env: env,
                        ex: null);

                    await ctx.Response.WriteAsJsonAsync(pdNull);
                    return;
                }

                // 1) FluentValidation -> ValidationProblemDetails (400)
                if (ex is ValidationException vex)
                {
                    ctx.Response.StatusCode = StatusCodes.Status400BadRequest;

                    var errors = vex.Errors
                        .Where(e => e is not null)
                        .GroupBy(e => e.PropertyName)
                        .ToDictionary(
                            g => string.IsNullOrWhiteSpace(g.Key) ? "general" : g.Key,
                            g => g.Select(e => e.ErrorMessage).Distinct().ToArray()
                        );

                    var vpd = new ValidationProblemDetails(errors)
                    {
                        Status = StatusCodes.Status400BadRequest,
                        Title = "Do?rulama hatas?",
                        Type = "https://httpstatuses.io/400",
                        Detail = "Bir veya daha fazla do?rulama hatas? olu?tu.",
                        Instance = ctx.Request.Path
                    };

                    vpd.Extensions["traceId"] = traceId;
                    vpd.Extensions["correlationId"] = correlationId;
                    vpd.Extensions["timestampUtc"] = DateTime.UtcNow;

                    if (env.IsDevelopment())
                        vpd.Extensions["stackTrace"] = ex.StackTrace;

                    await ctx.Response.WriteAsJsonAsync(vpd);
                    return;
                }

                // 2) Framework / unexpected exception mapping
                var (status, title, typeUrl, detailText) = ex switch
                {
                    UnauthorizedAccessException => (
                        StatusCodes.Status401Unauthorized,
                        "Yetkisiz eri?im",
                        "https://httpstatuses.io/401",
                        ex.Message
                    ),

                    KeyNotFoundException => (
                        StatusCodes.Status404NotFound,
                        "Bulunamad?",
                        "https://httpstatuses.io/404",
                        ex.Message
                    ),

                    OperationCanceledException => (
                        ClientClosedRequest,
                        "?stek iptal edildi",
                        "https://httpstatuses.io/499",
                        ex.Message
                    ),

                    _ => (
                        StatusCodes.Status500InternalServerError,
                        "Beklenmeyen hata",
                        "https://httpstatuses.io/500",
                        env.IsDevelopment() ? ex.Message : "Beklenmeyen bir hata olu?tu."
                    )
                };

                ctx.Response.StatusCode = status;

                var pd = CreateProblemDetails(
                    status: status,
                    title: title,
                    type: typeUrl,
                    detail: detailText,
                    instance: ctx.Request.Path,
                    traceId: traceId,
                    correlationId: correlationId,
                    env: env,
                    ex: ex);

                await ctx.Response.WriteAsJsonAsync(pd);
            });
        });

        return app;
    }

    private static ProblemDetails CreateProblemDetails(
        int status,
        string title,
        string type,
        string? detail,
        string instance,
        string traceId,
        string? correlationId,
        IWebHostEnvironment env,
        Exception? ex)
    {
        var pd = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = type,
            Detail = detail,
            Instance = instance
        };

        pd.Extensions["traceId"] = traceId;
        pd.Extensions["correlationId"] = correlationId;
        pd.Extensions["timestampUtc"] = DateTime.UtcNow;

        if (env.IsDevelopment() && ex is not null)
            pd.Extensions["stackTrace"] = ex.StackTrace;

        return pd;
    }
}
