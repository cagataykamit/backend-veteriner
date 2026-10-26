using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Prescriptions.Commands.Create;
using Backend.Veteriner.Application.Prescriptions.Commands.Update;
using Backend.Veteriner.Application.Prescriptions.Commands.UpdateHbs;
using Backend.Veteriner.Application.Prescriptions.Queries.GetHbsData;
using Backend.Veteriner.Application.Hbs.Contracts;
using Backend.Veteriner.Domain.Hbs;
using Backend.Veteriner.Application.Prescriptions.Contracts.Dtos;
using Backend.Veteriner.Application.Prescriptions.Queries.GetById;
using Backend.Veteriner.Application.Prescriptions.Queries.GetList;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>Clinical prescription records for pets (optional links to examination and/or treatment).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/prescriptions")]
[Produces("application/json")]
[Authorize]
public sealed class PrescriptionsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public PrescriptionsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Prescriptions.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] CreatePrescriptionCommand cmd, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

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
    [Authorize(Policy = PermissionCatalog.Prescriptions.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdatePrescriptionBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (body.Id is { } bid && bid != Guid.Empty && bid != id)
            return Result.Failure("Prescriptions.RouteIdMismatch", "Route id ile body id uyusmuyor.").ToActionResult(this);

        var cmd = new UpdatePrescriptionCommand(
            id,
            body.ClinicId,
            body.PetId,
            body.ExaminationId,
            body.TreatmentId,
            body.PrescribedAtUtc,
            body.Title,
            body.Content,
            body.Notes,
            body.FollowUpDateUtc,
            body.Items);

        var result = await _mediator.Send(cmd, ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Prescriptions.Read)]
    [ProducesResponseType(typeof(PrescriptionDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetPrescriptionByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// HBS e-Reçete ekranına girilecek hazır veri (alan alan, kopyalanabilir). Sahip kimlik no tam değeri yalnız <c>Clients.Update</c> ile.
    /// Her okuma denetim kaydına yazılır. Bkz. docs/HBS_ASSISTANT_API_CONTRACT.md bölüm 4.
    /// </summary>
    [HttpGet("{id:guid}/hbs-data")]
    [Authorize(Policy = PermissionCatalog.Prescriptions.Read)]
    [ProducesResponseType(typeof(HbsDataDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHbsData([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetPrescriptionHbsDataQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// e-Reçete geri kaydı (durum, e-Reçete No, "Gerekmiyor" gerekçesi). Kesinleşmiş muayeneye bağlı reçetede de çalışır; klinik içerik değişmez.
    /// Bkz. docs/HBS_ASSISTANT_API_CONTRACT.md bölüm 5.
    /// </summary>
    [HttpPatch("{id:guid}/hbs")]
    [Authorize(Policy = PermissionCatalog.Prescriptions.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(HbsEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateHbs([FromRoute] Guid id, [FromBody] UpdateHbsEntryBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new UpdatePrescriptionHbsCommand(id, body.Status, body.ReferenceNumber, body.NotRequiredReason), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Paged list. <c>search</c> (or <c>page.search</c>): title, content, notes, and pet/client name match (same pattern as treatments).
    /// Sorting: <c>sort=prescribedAtUtc</c>, <c>order=asc|desc</c>; other sort values return 400. Without sort: newest first.
    /// <c>hbsStatus</c>: HBS durumu süzgeci (<c>NotEntered|Entered|NotRequired</c>); satırda <c>hbsStatus</c> döner.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Prescriptions.Read)]
    [ProducesResponseType(typeof(PagedResult<PrescriptionListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] PageRequest page,
        [FromQuery] string? search = null,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] DateTime? dateFromUtc = null,
        [FromQuery] DateTime? dateToUtc = null,
        [FromQuery] HbsEntryStatus? hbsStatus = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var paging = PageRequestQuery.BindFromQuery(Request.Query, page);
        var result = await _mediator.Send(
            new GetPrescriptionsListQuery(
                PageRequestQuery.WithMergedSearch(paging, search),
                clinicId,
                petId,
                dateFromUtc,
                dateToUtc,
                hbsStatus),
            ct);
        return result.ToActionResult(this);
    }
}
