using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Backend.Veteriner.Infrastructure.Web;

public sealed class CurrentUserPermissionChecker : ICurrentUserPermissionChecker
{
    private readonly IHttpContextAccessor _http;

    public CurrentUserPermissionChecker(IHttpContextAccessor http) => _http = http;

    public bool HasPermission(string permissionCode)
    {
        if (string.IsNullOrEmpty(permissionCode))
            return false;
        var user = _http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return false;
        foreach (var c in user.FindAll("permission"))
        {
            if (string.Equals(c.Value, permissionCode, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
