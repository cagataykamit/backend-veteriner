using Asp.Versioning;
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
using Backend.Veteriner.Application.Reminders.Rules;
using Backend.Veteriner.Domain.Reminders;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

public sealed record UpdateClinicReminderRulesBody(IReadOnlyList<ClinicReminderRuleUpdate>? Rules);

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

    /// <summary>Kliniğin otomatik hatırlatıcı kuralları (TIER-003). Paketten bağımsız okunur; açmak paket ister.</summary>
    [HttpGet("clinics/{clinicId:guid}/rules")]
    [Authorize(Policy = PermissionCatalog.Reminders.Read)]
    [ProducesResponseType(typeof(ClinicReminderRulesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRules([FromRoute] Guid clinicId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new GetClinicReminderRulesQuery(clinicId), ct)).ToActionResult(this);
    }

    /// <summary>Listede olmayan kural değişmez; 403 <c>Subscriptions.FeatureNotInPlan</c>: kural açmak pakette yok.</summary>
    [HttpPut("clinics/{clinicId:guid}/rules")]
    [Authorize(Policy = PermissionCatalog.Reminders.Manage)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClinicReminderRulesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PutRules([FromRoute] Guid clinicId, [FromBody] UpdateClinicReminderRulesBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new UpdateClinicReminderRulesCommand(clinicId, body.Rules), ct)).ToActionResult(this);
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
        [FromQuery] Guid? clinicId = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var paging = PageRequestQuery.BindFromQuery(Request.Query, page);
        var result = await _mediator.Send(
            new GetReminderDispatchLogsQuery(paging, reminderType, status, fromUtc, toUtc, clinicId),
            ct);
        return result.ToActionResult(this);
    }
}
