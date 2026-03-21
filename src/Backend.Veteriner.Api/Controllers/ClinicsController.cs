using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Clinics.Commands.Create;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Queries.GetById;
using Backend.Veteriner.Application.Clinics.Queries.GetList;
using Backend.Veteriner.Application.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Klinik yönetimi. Kiracı: JWT <c>tenant_id</c> (login/refresh) veya sorgu <c>tenantId</c> (geçiş).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/clinics")]
[Produces("application/json")]
[Authorize]
public sealed class ClinicsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ClinicsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Clinics.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateClinicCommand cmd, CancellationToken ct)
    {
        if (this.ValidateBodyTenant(_tenantContext, cmd.TenantId) is { } forbid)
            return forbid;

        var result = await _mediator.Send(cmd, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var id = result.Value;
        return CreatedAtAction(
            nameof(GetById),
            new
            {
                version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0",
                id,
                tenantId = cmd.TenantId
            },
            id);
    }

    /// <summary>
    /// Klinik detayı. Kiracı JWT <c>tenant_id</c> veya sorgu <c>tenantId</c> (middleware).
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Clinics.Read)]
    [ProducesResponseType(typeof(ClinicDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out var tid, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetClinicByIdQuery(tid, id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Kiracıya göre sayfalı klinik listesi.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Clinics.Read)]
    [ProducesResponseType(typeof(PagedResult<ClinicListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetList([FromQuery] PageRequest page, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out var tid, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetClinicsListQuery(tid, page), ct);
        return result.ToActionResult(this);
    }
}
