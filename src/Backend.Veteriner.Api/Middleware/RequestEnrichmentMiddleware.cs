using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Serilog.Context;

namespace Backend.Veteriner.Api.Middleware;

public sealed class RequestEnrichmentMiddleware
{
    private readonly RequestDelegate _next;

    public RequestEnrichmentMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext context)
    {
        // Client IP (ForwardedHeaders sonras�)
        var clientIp = context.Connection.RemoteIpAddress?.ToString();

        // Fallback: X-Forwarded-For (ilk IP�yi al)
        if (string.IsNullOrWhiteSpace(clientIp) &&
            context.Request.Headers.TryGetValue("X-Forwarded-For", out var fwd) &&
            !string.IsNullOrWhiteSpace(fwd))
        {
            clientIp = fwd.ToString().Split(',')[0].Trim();
        }

        // ::1 yerine 127.0.0.1 gibi okunur k�lmak istersen:
        if (clientIp == "::1") clientIp = "127.0.0.1";

        // JWT claim'lerinden kullan�c�
        var user = context.User;
        var userId =
               user.FindFirst("sub")?.Value
            ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        var email =
               user.FindFirst("email")?.Value
            ?? user.FindFirstValue(JwtRegisteredClaimNames.Email)
            ?? user.FindFirstValue(ClaimTypes.Email);

        using (LogContext.PushProperty("ClientIp", clientIp ?? "unknown"))
        using (LogContext.PushProperty("UserId", userId ?? "anonymous"))
        using (LogContext.PushProperty("UserEmail", email ?? "anonymous"))
        {
            await _next(context);
        }
    }
}
