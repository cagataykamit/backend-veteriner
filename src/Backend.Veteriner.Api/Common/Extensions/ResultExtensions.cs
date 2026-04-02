using Backend.Veteriner.Domain.Shared;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Common.Extensions;

public static class ResultExtensions
{
    public static IActionResult ToActionResult(this Result result, ControllerBase controller)
    {
        if (result.IsSuccess)
        {
            return controller.NoContent();
        }

        return MapFailure(controller, result.Error);
    }

    public static IActionResult ToActionResult<T>(this Result<T> result, ControllerBase controller)
    {
        if (result.IsSuccess)
        {
            return controller.Ok(result.Value);
        }

        return MapFailure(controller, result.Error);
    }

    private static IActionResult MapFailure(ControllerBase controller, Error error)
    {
        var (statusCode, type) = MapStatusCode(error.Code);
        var traceId = Activity.Current?.Id ?? controller.HttpContext.TraceIdentifier;
        var correlationId = ResolveCorrelationId(controller.HttpContext, traceId);

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = ResolveProblemTitle(error.Code, statusCode),
            Detail = string.IsNullOrWhiteSpace(error.Message)
                ? "A business rule was violated."
                : error.Message,
            Type = type,
            Instance = controller.HttpContext.Request.Path
        };

        if (!string.IsNullOrWhiteSpace(error.Code))
        {
            problem.Extensions["code"] = error.Code;
        }
        problem.Extensions["traceId"] = traceId;
        problem.Extensions["correlationId"] = correlationId;
        problem.Extensions["timestampUtc"] = DateTime.UtcNow;

        return controller.StatusCode(statusCode, problem);
    }

    private static string ResolveCorrelationId(HttpContext httpContext, string fallback)
    {
        return
            (httpContext.Items.TryGetValue("X-Correlation-ID", out var v) ? v?.ToString() : null)
            ?? (httpContext.Items.TryGetValue("CorrelationId", out var legacy) ? legacy?.ToString() : null)
            ?? (httpContext.Request.Headers.TryGetValue("X-Correlation-ID", out var cid) ? cid.ToString() : null)
            ?? fallback;
    }

    private static (int StatusCode, string Type) MapStatusCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return (StatusCodes.Status400BadRequest, "https://httpstatuses.io/400");
        }

        var normalized = code.ToLowerInvariant();

        if (normalized.Contains("duplicate"))
        {
            return (StatusCodes.Status409Conflict, "https://httpstatuses.io/409");
        }

        if (normalized.Contains("notfound"))
        {
            return (StatusCodes.Status404NotFound, "https://httpstatuses.io/404");
        }

        if (normalized.Contains("forbidden"))
        {
            return (StatusCodes.Status403Forbidden, "https://httpstatuses.io/403");
        }

        if (normalized.Contains("tenantinactive"))
        {
            return (StatusCodes.Status403Forbidden, "https://httpstatuses.io/403");
        }

        if (normalized.Contains("unauthorized") || normalized.Contains("unauthenticated"))
        {
            return (StatusCodes.Status401Unauthorized, "https://httpstatuses.io/401");
        }

        if (normalized.Contains("validation"))
        {
            return (StatusCodes.Status400BadRequest, "https://httpstatuses.io/400");
        }

        return (StatusCodes.Status400BadRequest, "https://httpstatuses.io/400");
    }

    /// <summary>
    /// ModelState/FluentValidation ile aynı dil ve anlam hizası; kod öncelikli (validation vs HTTP durumu).
    /// </summary>
    private static string ResolveProblemTitle(string? code, int statusCode)
    {
        var normalized = code?.ToLowerInvariant() ?? "";

        if (normalized.Contains("validation"))
            return "Doğrulama hatası";

        return statusCode switch
        {
            StatusCodes.Status401Unauthorized => "Yetkisiz erişim",
            StatusCodes.Status403Forbidden => "Erişim reddedildi",
            StatusCodes.Status404NotFound => "Bulunamadı",
            StatusCodes.Status409Conflict => "Çakışma",
            StatusCodes.Status400BadRequest when normalized.Contains("unauthorized")
                || normalized.Contains("unauthenticated") => "Yetkisiz erişim",
            _ => "İş kuralı ihlali"
        };
    }
}

