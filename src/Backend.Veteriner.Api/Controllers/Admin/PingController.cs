using Backend.Veteriner.Api.Auth;
using Backend.Veteriner.Application.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers.Admin;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/ping")]
public sealed class PingController : ControllerBase
{
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult Ping()
        => Ok(new { ok = true, message = "pong" });

    [HttpGet("users-read")]
    [HasPermission(PermissionCatalog.Users.Read)]
    [Produces("application/json")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult UsersRead()
        => Ok(new { ok = true, permission = PermissionCatalog.Users.Read });

    [HttpGet("test-feature")]
    [HasPermission(PermissionCatalog.Test.FeatureRun)]
    [Produces("application/json")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult TestFeature()
        => Ok(new { ok = true, permission = PermissionCatalog.Test.FeatureRun });


    [HttpGet("forbidden-test")]
    [HasPermission(PermissionCatalog.Test.ForbiddenProbe)]
    [Produces("application/json")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult ForbiddenTest()
    => Ok(new { ok = true, permission = PermissionCatalog.Test.ForbiddenProbe });
}