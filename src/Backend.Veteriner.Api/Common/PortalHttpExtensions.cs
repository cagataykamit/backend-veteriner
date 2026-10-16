using System.IdentityModel.Tokens.Jwt;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Common;

/// <summary>Portal denetleyicilerinde oturumdaki hasta sahibi hesabı (portal şeması + <c>principal_type=owner</c>).</summary>
public static class PortalHttpExtensions
{
    public static bool TryGetOwnerAccountId(this ControllerBase controller, out Guid ownerAccountId, out IActionResult? problem)
    {
        var user = controller.User;
        var isOwner = user.HasClaim(OwnerPortalAuth.PrincipalTypeClaim, OwnerPortalAuth.OwnerPrincipalType);
        if (isOwner && Guid.TryParse(user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out ownerAccountId))
        {
            problem = null;
            return true;
        }

        ownerAccountId = default;
        problem = Result.Failure("Portal.Auth.Unauthorized.AccountContextMissing", "Portal hesabı token içinde bulunamadı.")
            .ToActionResult(controller);
        return false;
    }
}
