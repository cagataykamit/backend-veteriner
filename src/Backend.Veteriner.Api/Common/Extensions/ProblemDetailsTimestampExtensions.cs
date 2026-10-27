using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Common.Extensions;

public static class ProblemDetailsTimestampExtensions
{
    /// <summary>Hata gövdesine <c>timestampUtc</c> yazar; saat DI'daki <see cref="TimeProvider"/>'dan okunur (B4).</summary>
    public static void AddTimestamp(this ProblemDetails problem, HttpContext httpContext)
        => problem.Extensions["timestampUtc"] =
            httpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
}
