using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Backend.Veteriner.Application.Common.Constants;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace Backend.Veteriner.Api.Middleware;

public static class ExceptionHandlingExtensions
{
    private const int ClientClosedRequest = 499;

    /// <summary>FluentValidation <see cref="ValidationException"/> için JSON; Türkçe ve alan adları camelCase.</summary>
    private static readonly JsonSerializerOptions ValidationProblemJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

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
                    ctx.Items.TryGetValue(Correlation.HeaderName, out var v) ? v?.ToString() :
                    ctx.Items.TryGetValue("CorrelationId", out var legacy) ? legacy?.ToString() :
                    ctx.Request.Headers.TryGetValue(Correlation.HeaderName, out var cid) ? cid.ToString() :
                    traceId;

                if (ex is not null)
                    Log.Error(ex, "Unhandled exception. TraceId={TraceId} CorrelationId={CorrelationId}", traceId, correlationId);
                else
                    Log.Error("Unhandled error without exception. TraceId={TraceId} CorrelationId={CorrelationId}", traceId, correlationId);

                ctx.Response.ContentType = "application/problem+json";

                // Exception null ise bile yanıt gövdesi üretilir
                if (ex is null)
                {
                    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;

                    var pdNull = CreateProblemDetails(
                        status: StatusCodes.Status500InternalServerError,
                        title: "Beklenmeyen hata",
                        type: "https://httpstatuses.io/500",
                        detail: env.IsDevelopment()
                            ? "Exception alınamadı (null)."
                            : "Beklenmeyen bir hata oluştu.",
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
                        .GroupBy(e => ToCamelCasePropertyPath(e.PropertyName))
                        .ToDictionary(
                            g => string.IsNullOrWhiteSpace(g.Key) ? "general" : g.Key,
                            g => g.Select(e => e.ErrorMessage).Distinct().ToArray()
                        );

                    var vpd = new ValidationProblemDetails(errors)
                    {
                        Status = StatusCodes.Status400BadRequest,
                        Title = "Doğrulama hatası",
                        Type = "https://httpstatuses.io/400",
                        Detail = "Gönderilen bilgiler doğrulanamadı. Aşağıdaki alanları kontrol edin.",
                        Instance = ctx.Request.Path
                    };

                    vpd.Extensions["traceId"] = traceId;
                    vpd.Extensions["correlationId"] = correlationId;
                    vpd.Extensions["code"] = "Validation.FluentValidation";
                    vpd.Extensions["timestampUtc"] = DateTime.UtcNow;

                    if (env.IsDevelopment())
                        vpd.Extensions["stackTrace"] = ex.StackTrace;

                    await ctx.Response.WriteAsJsonAsync(vpd, ValidationProblemJsonOptions);
                    return;
                }

                // 2) Framework / unexpected exception mapping
                var (status, title, typeUrl, detailText) = ex switch
                {
                    UnauthorizedAccessException => (
                        StatusCodes.Status401Unauthorized,
                        "Yetkisiz erişim",
                        "https://httpstatuses.io/401",
                        ex.Message
                    ),

                    KeyNotFoundException => (
                        StatusCodes.Status404NotFound,
                        "Bulunamadı",
                        "https://httpstatuses.io/404",
                        ex.Message
                    ),

                    OperationCanceledException => (
                        ClientClosedRequest,
                        "İstek iptal edildi",
                        "https://httpstatuses.io/499",
                        ex.Message
                    ),

                    _ => (
                        StatusCodes.Status500InternalServerError,
                        "Beklenmeyen hata",
                        "https://httpstatuses.io/500",
                        env.IsDevelopment() ? ex.Message : "Beklenmeyen bir hata oluştu."
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
        pd.Extensions["code"] = status switch
        {
            StatusCodes.Status401Unauthorized => "Errors.Unauthorized",
            StatusCodes.Status404NotFound => "Errors.NotFound",
            StatusCodes.Status500InternalServerError => "Errors.Unhandled",
            ClientClosedRequest => "Errors.RequestCancelled",
            _ => "Errors.Unhandled"
        };
        pd.Extensions["timestampUtc"] = DateTime.UtcNow;

        if (env.IsDevelopment() && ex is not null)
            pd.Extensions["stackTrace"] = ex.StackTrace;

        return pd;
    }

    /// <summary>
    /// FluentValidation <c>PropertyName</c> genelde <c>Phone</c> biçimindedir; JSON <c>errors.phone</c> ile uyum için camelCase yapılır.
    /// İç içe özelliklerde her segment ayrı dönüştürülür.
    /// </summary>
    private static string ToCamelCasePropertyPath(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            return "general";

        var segments = propertyName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
            return "general";

        var sb = new StringBuilder();
        for (var i = 0; i < segments.Length; i++)
        {
            if (i > 0)
                sb.Append('.');
            sb.Append(ToCamelCaseSegment(segments[i]));
        }

        return sb.ToString();
    }

    private static string ToCamelCaseSegment(string segment)
    {
        if (string.IsNullOrEmpty(segment))
            return segment;
        if (segment.Length == 1)
            return segment.ToLowerInvariant();
        return char.ToLowerInvariant(segment[0]) + segment[1..];
    }
}
