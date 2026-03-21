using Backend.Veteriner.Domain.Shared;
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

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = "Business rule violation",
            Detail = string.IsNullOrWhiteSpace(error.Message)
                ? "A business rule was violated."
                : error.Message,
            Type = type
        };

        if (!string.IsNullOrWhiteSpace(error.Code))
        {
            problem.Extensions["code"] = error.Code;
        }

        return controller.StatusCode(statusCode, problem);
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
}

