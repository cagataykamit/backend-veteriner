using System.Text.Json.Serialization;
using Asp.Versioning;
using Backend.Veteriner.Api.Auth;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.CustomDevelopment;
using Backend.Veteriner.Domain.CustomDevelopment;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

public sealed record ChangeCustomDevelopmentRequestStatusBody(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] CustomDevelopmentRequestStatus? Status,
    string? DecisionReason,
    string? ResponseNote);

/// <summary>
/// Size özel geliştirme talepleri, platform tarafı (TIER-010): yalnız platform yöneticisi (<c>platform_admin</c> claim'i), kiracılar arası;
/// klinik rolleri 403. Sözleşme: docs/CUSTOM_DEVELOPMENT_REQUESTS_API_CONTRACT.md §4.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/custom-development-requests")]
[Authorize(Policy = AuthorizationPolicyNames.PlatformAdmin)]
[PlatformTenantScope]
[Produces("application/json")]
public sealed class CustomDevelopmentRequestsAdminController : ControllerBase
{
    private readonly IMediator _mediator;

    public CustomDevelopmentRequestsAdminController(IMediator mediator) => _mediator = mediator;

    /// <summary>Tüm kiracıların talepleri, yeniden eskiye. <c>status</c> ya da <c>openOnly</c> ile filtre.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AdminCustomDevelopmentRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] CustomDevelopmentRequestStatus? status = null,
        [FromQuery] bool openOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = GetAdminCustomDevelopmentRequestsQuery.DefaultPageSize,
        [FromQuery] CustomDevelopmentTargetKind? targetKind = null,
        CancellationToken ct = default)
        => (await _mediator.Send(new GetAdminCustomDevelopmentRequestsQuery(status, openOnly, page, pageSize, targetKind), ct)).ToActionResult(this);

    /// <summary>Durumu ilerletir; kapsam dışında gerekçe zorunlu. Denetim kaydına yazılır.</summary>
    [HttpPost("{id:guid}/status")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AdminCustomDevelopmentRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeStatus([FromRoute] Guid id, [FromBody] ChangeCustomDevelopmentRequestStatusBody body, CancellationToken ct)
        => (await _mediator.Send(new ChangeCustomDevelopmentRequestStatusCommand(id, body.Status, body.DecisionReason, body.ResponseNote), ct))
            .ToActionResult(this);
}
