using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Appointments.Commands.Cancel;
using Backend.Veteriner.Application.Appointments.Commands.Complete;
using Backend.Veteriner.Application.Appointments.Commands.Create;
using Backend.Veteriner.Application.Appointments.Commands.Reschedule;
using Backend.Veteriner.Application.Appointments.Contracts;
using Backend.Veteriner.Application.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Appointments.Queries.GetById;
using Backend.Veteriner.Application.Appointments.Queries.GetList;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Appointments;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Randevu yönetimi. Kiracı JWT <c>tenant_id</c> veya sorgu <c>tenantId</c>; filtreler sorguda.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/appointments")]
[Produces("application/json")]
[Authorize]
public sealed class AppointmentsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public AppointmentsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Appointments.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateAppointmentCommand cmd, CancellationToken ct)
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

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Appointments.Read)]
    [ProducesResponseType(typeof(AppointmentDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out var tid, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetAppointmentByIdQuery(tid, id), ct);
        return result.ToActionResult(this);
    }

    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Appointments.Read)]
    [ProducesResponseType(typeof(PagedResult<AppointmentListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetList(
        [FromQuery] PageRequest page,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] AppointmentStatus? status = null,
        [FromQuery] DateTime? dateFromUtc = null,
        [FromQuery] DateTime? dateToUtc = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out var tid, out var problem))
            return problem!;
        var result = await _mediator.Send(
            new GetAppointmentsListQuery(tid, page, clinicId, petId, status, dateFromUtc, dateToUtc),
            ct);
        return result.ToActionResult(this);
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PermissionCatalog.Appointments.Cancel)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(
        [FromRoute] Guid id,
        [FromBody] CancelAppointmentBody? body,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out var tid, out var problem))
            return problem!;

        var result = await _mediator.Send(new CancelAppointmentCommand(tid, id, body?.Reason), ct);
        return result.ToActionResult(this);
    }

    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = PermissionCatalog.Appointments.Complete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Complete([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out var tid, out var problem))
            return problem!;

        var result = await _mediator.Send(new CompleteAppointmentCommand(tid, id), ct);
        return result.ToActionResult(this);
    }

    [HttpPost("{id:guid}/reschedule")]
    [Authorize(Policy = PermissionCatalog.Appointments.Reschedule)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reschedule(
        [FromRoute] Guid id,
        [FromBody] RescheduleAppointmentBody? body,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out var tid, out var problem))
            return problem!;

        if (body is null)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Geçersiz istek",
                detail: "JSON gövdesi ve scheduledAtUtc alanı zorunludur.");
        }

        var result = await _mediator.Send(
            new RescheduleAppointmentCommand(tid, id, body.ScheduledAtUtc),
            ct);
        return result.ToActionResult(this);
    }
}
