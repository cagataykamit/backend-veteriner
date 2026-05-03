using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Api.Contracts;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Commands.Activate;
using Backend.Veteriner.Application.Clinics.Commands.Create;
using Backend.Veteriner.Application.Clinics.Commands.Deactivate;
using Backend.Veteriner.Application.Clinics.Commands.Update;
using Backend.Veteriner.Application.Clinics.Commands.WorkingHours.UpdateClinicWorkingHours;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Queries.GetById;
using Backend.Veteriner.Application.Clinics.Queries.GetList;
using Backend.Veteriner.Application.Clinics.Queries.WorkingHours.GetClinicWorkingHours;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Klinik yönetimi. Kiracı JWT <c>tenant_id</c>; handler’da <see cref="ITenantContext"/>.
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
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(cmd, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var id = result.Value;
        return CreatedAtAction(
            nameof(GetById),
            new
            {
                version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0",
                id
            },
            id);
    }

    /// <summary>Klinik haftalık çalışma saatleri. Kayıt yoksa varsayılan program döner.</summary>
    [HttpGet("{id:guid}/working-hours")]
    [Authorize(Policy = PermissionCatalog.Clinics.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<ClinicWorkingHourDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWorkingHours([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetClinicWorkingHoursQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Klinik haftalık çalışma saatlerini kaydeder (tam 7 gün).</summary>
    [HttpPut("{id:guid}/working-hours")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(IReadOnlyList<ClinicWorkingHourDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PutWorkingHours(
        [FromRoute] Guid id,
        [FromBody] UpdateClinicWorkingHoursRequest? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Result<IReadOnlyList<ClinicWorkingHourDto>>.Failure(
                    "Clinics.WorkingHours.Validation.InvalidRequestBody",
                    "Istek govdesi bos veya hatali JSON.")
                .ToActionResult(this);
        }

        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new UpdateClinicWorkingHoursCommand(id, body.Items), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Klinik detayı.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Clinics.Read)]
    [ProducesResponseType(typeof(ClinicDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetClinicByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Kiracıya göre sayfalı klinik listesi.</summary>
    /// <remarks><c>PageRequest.search</c>, <c>sort</c> ve <c>order</c> şu an işlenmez.</remarks>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Clinics.Read)]
    [ProducesResponseType(typeof(PagedResult<ClinicListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetList([FromQuery] PageRequest page, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetClinicsListQuery(page), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Klinik bilgilerini (ad, şehir, iletişim/profil alanları) günceller.</summary>
    /// <remarks>Route id kaynak doğrudur; body.id verilmişse route ile aynı olmalıdır.</remarks>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClinicDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateClinicBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (body.Id is { } bodyId && bodyId != Guid.Empty && bodyId != id)
            return Result.Failure("Clinics.RouteIdMismatch", "Route id ile body id uyuşmuyor.").ToActionResult(this);

        var cmd = new UpdateClinicCommand(
            id,
            body.Name,
            body.City,
            body.Phone,
            body.Email,
            body.Address,
            body.Description);
        var result = await _mediator.Send(cmd, ct);
        return result.ToActionResult(this);
    }

    /// <summary>Kliniği pasife alır (idempotent).</summary>
    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [ProducesResponseType(typeof(DeactivateClinicResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new DeactivateClinicCommand(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Kliniği yeniden aktifleştirir (idempotent).</summary>
    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [ProducesResponseType(typeof(ActivateClinicResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activate([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new ActivateClinicCommand(id), ct);
        return result.ToActionResult(this);
    }
}
