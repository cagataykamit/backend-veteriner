using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Examinations.Commands.Create;
using Backend.Veteriner.Application.Examinations.Commands.Update;
using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Application.Examinations.Queries.GetById;
using Backend.Veteriner.Application.Examinations.Queries.GetList;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Muayene (klinik) kayıtları. Kiracı yalnızca <see cref="ITenantContext"/> ile çözülür.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/examinations")]
[Produces("application/json")]
[Authorize]
public sealed class ExaminationsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ExaminationsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Examinations.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    /// <remarks>Kanonik alan <c>visitReason</c>. Eski istemciler <c>complaint</c> gönderebilir (yalnızca <c>visitReason</c> boşken kullanılır). Yanıtta yalnızca <c>visitReason</c> döner.</remarks>
    public async Task<IActionResult> Create([FromBody] CreateExaminationBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var visitReason = ExaminationVisitReasonResolver.Resolve(body.VisitReason, body.Complaint);
        var findings = body.Findings ?? string.Empty;

        var cmd = new CreateExaminationCommand(
            body.ClinicId,
            body.PetId,
            body.AppointmentId,
            body.ExaminedAtUtc,
            visitReason,
            findings,
            body.Assessment,
            body.Notes);

        var result = await _mediator.Send(cmd, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var id = result.Value;
        return CreatedAtAction(
            nameof(GetById),
            new { version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0", id },
            id);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Examinations.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    /// <remarks>Kanonik alan <c>visitReason</c>. Eski istemciler <c>complaint</c> gönderebilir. Yanıtta yalnızca <c>visitReason</c>.</remarks>
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateExaminationBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (body.Id is { } bid && bid != Guid.Empty && bid != id)
            return Result.Failure("Examinations.RouteIdMismatch", "Route id ile body id uyusmuyor.").ToActionResult(this);

        var visitReason = ExaminationVisitReasonResolver.Resolve(body.VisitReason, body.Complaint);
        var findings = body.Findings ?? string.Empty;

        var cmd = new UpdateExaminationCommand(
            id,
            body.ClinicId,
            body.PetId,
            body.AppointmentId,
            body.ExaminedAtUtc,
            visitReason,
            findings,
            body.Assessment,
            body.Notes);

        var result = await _mediator.Send(cmd, ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Examinations.Read)]
    [ProducesResponseType(typeof(ExaminationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetExaminationByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Sayfalı muayene listesi. <c>search</c> (veya <c>page.search</c>): başvuru nedeni, bulgular, değerlendirme, notlar; müşteri metin alanlarında; hayvan ad/tür/ırk (hayvan listesi ile aynı küme). <c>sort</c>/<c>order</c> işlenmez.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Examinations.Read)]
    [ProducesResponseType(typeof(PagedResult<ExaminationListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetList(
        [FromQuery] PageRequest page,
        [FromQuery] string? search = null,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] Guid? appointmentId = null,
        [FromQuery] DateTime? dateFromUtc = null,
        [FromQuery] DateTime? dateToUtc = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new GetExaminationsListQuery(
                PageRequestQuery.WithMergedSearch(page, search),
                clinicId,
                petId,
                appointmentId,
                dateFromUtc,
                dateToUtc),
            ct);
        return result.ToActionResult(this);
    }
}
