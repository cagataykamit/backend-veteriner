using Microsoft.AspNetCore.Authorization;

namespace Backend.Veteriner.Api.Auth
{
    public sealed record PermissionRequirement(string Code) : IAuthorizationRequirement;
}
