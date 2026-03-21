using System.Security.Claims;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Microsoft.AspNetCore.Http;

namespace Backend.Veteriner.Infrastructure.Web;

public sealed class HttpAuditContext : IAuditContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpAuditContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? _httpContextAccessor.HttpContext?.User?.FindFirstValue("sub");

            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string? Route => _httpContextAccessor.HttpContext?.Request?.Path.Value;

    public string? HttpMethod => _httpContextAccessor.HttpContext?.Request?.Method;

    public string? IpAddress => _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString();

    public string? UserAgent => _httpContextAccessor.HttpContext?.Request?.Headers.UserAgent.ToString();

    public string? CorrelationId =>
        _httpContextAccessor.HttpContext?.Items[Correlation.HeaderName]?.ToString()
        ?? _httpContextAccessor.HttpContext?.TraceIdentifier;
}