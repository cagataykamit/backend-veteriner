using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Reminders.Commands.UpdateSettings;
using Backend.Veteriner.Application.Reminders.Contracts.Dtos;
using Backend.Veteriner.Application.Reminders.Contracts.Requests;
using Backend.Veteriner.Application.Reminders.Queries.GetLogs;
using Backend.Veteriner.Application.Reminders.Queries.GetSettings;
using Backend.Veteriner.Domain.Reminders;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/reminders")]
[Produces("application/json")]
[Authorize]
public sealed class RemindersController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public RemindersController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpGet("settings")]
    [Authorize(Policy = PermissionCatalog.Reminders.Read)]
    [ProducesResponseType(typeof(ReminderSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetReminderSettingsQuery(), ct);
        return result.ToActionResult(this);
    }

    [HttpPut("settings")]
    [Authorize(Policy = PermissionCatalog.Reminders.Manage)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ReminderSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> PutSettings([FromBody] UpdateReminderSettingsRequest request, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new UpdateReminderSettingsCommand(
                request.AppointmentRemindersEnabled,
                request.AppointmentReminderHoursBefore,
                request.VaccinationRemindersEnabled,
                request.VaccinationReminderDaysBefore,
                request.EmailChannelEnabled),
            ct);
        return result.ToActionResult(this);
    }

    [HttpGet("logs")]
    [Authorize(Policy = PermissionCatalog.Reminders.Read)]
    [ProducesResponseType(typeof(PagedResult<ReminderDispatchLogItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetLogs(
        [FromQuery] PageRequest page,
        [FromQuery] ReminderType? reminderType = null,
        [FromQuery] ReminderDispatchStatus? status = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new GetReminderDispatchLogsQuery(page, reminderType, status, fromUtc, toUtc),
            ct);
        return result.ToActionResult(this);
    }
}
