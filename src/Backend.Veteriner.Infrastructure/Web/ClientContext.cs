using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Microsoft.AspNetCore.Http;

namespace Backend.Veteriner.Infrastructure.Web;

public sealed class ClientContext : IClientContext
{
    private readonly IHttpContextAccessor _accessor;

    public ClientContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public string? IpAddress
        => _accessor.HttpContext?.Connection?.RemoteIpAddress?.ToString();

    public string? UserAgent
        => _accessor.HttpContext?.Request?.Headers["User-Agent"].ToString();

    public string? Path
        => _accessor.HttpContext?.Request?.Path.Value;

    public string? Method
        => _accessor.HttpContext?.Request?.Method;

    public string? CorrelationId
    => _accessor.HttpContext?.Items[Correlation.HeaderName]?.ToString()
       ?? _accessor.HttpContext?.TraceIdentifier;

    public Guid? UserId
    {
        get
        {
            var user = _accessor.HttpContext?.User;
            if (user is null)
                return null;

            var sub =
                user.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
                user.FindFirstValue("sub") ??
                user.FindFirstValue(ClaimTypes.NameIdentifier);

            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }
}