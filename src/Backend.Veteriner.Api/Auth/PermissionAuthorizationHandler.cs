using Microsoft.AspNetCore.Authorization;

namespace Backend.Veteriner.Api.Auth;

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        // Tekil "permission" claim'leri
        var singles = context.User.FindAll("permission").Select(c => c.Value);

        // CSV "permissions" claim'i
        var csv = context.User.FindAll("permissions")
            .SelectMany(c => (c.Value ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var set = new HashSet<string>(singles.Concat(csv), StringComparer.OrdinalIgnoreCase);
        if (set.Contains(requirement.Code))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
