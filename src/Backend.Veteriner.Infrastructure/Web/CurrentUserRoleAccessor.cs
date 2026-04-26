using System.Security.Claims;
using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Backend.Veteriner.Infrastructure.Web;

public sealed class CurrentUserRoleAccessor : ICurrentUserRoleAccessor
{
    private readonly IHttpContextAccessor _http;

    public CurrentUserRoleAccessor(IHttpContextAccessor http) => _http = http;

    public IReadOnlyList<string> GetRoleNames()
    {
        var user = _http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return Array.Empty<string>();

        var roles = user
            .FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var role in user.FindAll("role"))
        {
            if (string.IsNullOrWhiteSpace(role.Value))
                continue;
            if (!roles.Contains(role.Value, StringComparer.OrdinalIgnoreCase))
                roles.Add(role.Value);
        }

        return roles;
    }
}
