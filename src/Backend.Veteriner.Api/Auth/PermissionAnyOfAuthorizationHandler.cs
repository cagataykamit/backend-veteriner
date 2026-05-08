using Microsoft.AspNetCore.Authorization;

namespace Backend.Veteriner.Api.Auth;

/// <summary>
/// <see cref="PermissionAnyOfRequirement"/> için authorization handler.
/// Kullanıcının <c>permission</c> tekil veya <c>permissions</c> CSV claim'lerinde verilen kodlardan
/// herhangi biri varsa requirement'ı başarılı sayar. <see cref="PermissionAuthorizationHandler"/> ile
/// aynı claim okuma davranışını uygular; yalnızca eşleşme mantığı "any-of"a çevrilmiştir.
/// </summary>
public sealed class PermissionAnyOfAuthorizationHandler : AuthorizationHandler<PermissionAnyOfRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionAnyOfRequirement requirement)
    {
        if (requirement.Codes is null || requirement.Codes.Count == 0)
            return Task.CompletedTask;

        var singles = context.User.FindAll("permission").Select(c => c.Value);

        var csv = context.User.FindAll("permissions")
            .SelectMany(c => (c.Value ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var owned = new HashSet<string>(singles.Concat(csv), StringComparer.OrdinalIgnoreCase);

        foreach (var code in requirement.Codes)
        {
            if (!string.IsNullOrWhiteSpace(code) && owned.Contains(code))
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }
        }

        return Task.CompletedTask;
    }
}
