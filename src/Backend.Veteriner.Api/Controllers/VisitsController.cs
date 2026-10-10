using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Visits.Commands.Correct;
using Backend.Veteriner.Application.Visits.Commands.Create;
using Backend.Veteriner.Application.Visits.Commands.Restore;
using Backend.Veteriner.Application.Visits.Commands.SetUrgency;
using Backend.Veteriner.Application.Visits.Commands.Transition;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.Queries.GetById;
using Backend.Veteriner.Application.Visits.Queries.GetToday;
using Backend.Veteriner.Domain.Visits;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Geliş (Visit) kayıtları ve Bugün görünümü. Kiracı yalnızca <see cref="ITenantContext"/> ile çözülür.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/visits")]
[Produces("application/json")]
[Authorize]
public sealed class VisitsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public VisitsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Geliş kaydı. Yeni kayıt 201; tekrar istek (aynı randevu/hayvan için mevcut geliş) 200 ve <c>created: false</c>.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Visits.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(VisitCreateResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(VisitCreateResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] CreateVisitBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new CreateVisitCommand(body.ClinicId, body.PetId, body.AppointmentId, body.ResponsibleVeterinarianUserId, body.IsUrgent),
            ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var created = result.Value!;
        if (!created.Created)
            return Ok(created);

        return CreatedAtAction(
            nameof(GetById),
            new { version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0", id = created.Visit.Id },
            created);
    }

    /// <summary>Bekliyor → Devam ediyor. Zaten devam ediyorsa 200 ve mevcut kayıt.</summary>
    [HttpPost("{id:guid}/start")]
    [Authorize(Policy = PermissionCatalog.Visits.Update)]
    [ProducesResponseType(typeof(VisitDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Start([FromRoute] Guid id, CancellationToken ct)
        => Transition(id, VisitCareStatus.InProgress, ct);

    /// <summary>Devam ediyor → Tamamlandı. Zaten tamamlandıysa 200 ve mevcut kayıt.</summary>
    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = PermissionCatalog.Visits.Update)]
    [ProducesResponseType(typeof(VisitDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Complete([FromRoute] Guid id, CancellationToken ct)
        => Transition(id, VisitCareStatus.Completed, ct);

    /// <summary>Yanlış geliş veya yanlış durum düzeltmesi; gerekçe zorunlu, audit'e yazılır.</summary>
    [HttpPost("{id:guid}/corrections")]
    [Authorize(Policy = PermissionCatalog.Visits.Correct)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(VisitDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Correct([FromRoute] Guid id, [FromBody] CorrectVisitBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new CorrectVisitCommand(id, body.Reason ?? string.Empty, body.TargetCareStatus, body.MarkAsMistaken),
            ct);
        return result.ToActionResult(this);
    }

    /// <summary>Yanlış geliş işaretini gerekçeyle geri alır; aynı hayvan/randevu için başka geliş varsa 409, audit'e yazılır.</summary>
    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = PermissionCatalog.Visits.Correct)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(VisitDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore([FromRoute] Guid id, [FromBody] RestoreVisitBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new RestoreVisitCommand(id, body.Reason ?? string.Empty), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Acil işaretini koyar/kaldırır (basit bayrak, triage skoru yok); aynı değer 200 ve mevcut kayıt.</summary>
    [HttpPut("{id:guid}/urgency")]
    [Authorize(Policy = PermissionCatalog.Visits.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(VisitDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetUrgency([FromRoute] Guid id, [FromBody] SetVisitUrgencyBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new SetVisitUrgencyCommand(id, body.IsUrgent), ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Visits.Read)]
    [ProducesResponseType(typeof(VisitDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetVisitByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Günlük aksiyon yüzeyi: geliş sırası, planlı randevular ve aktif yatışlar (geçmişe dönük rapor değildir). <c>voided=true</c>: yalnızca o günün yanlış işaretlenen gelişleri.</summary>
    [HttpGet("today")]
    [Authorize(Policy = PermissionCatalog.Visits.Read)]
    [ProducesResponseType(typeof(TodayDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetToday(
        [FromQuery] Guid? clinicId,
        [FromQuery] DateOnly? localDate,
        [FromQuery] bool voided,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetVisitsTodayQuery(clinicId, localDate, voided), ct);
        return result.ToActionResult(this);
    }

    private async Task<IActionResult> Transition(Guid id, VisitCareStatus target, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new TransitionVisitCommand(id, target), ct);
        return result.ToActionResult(this);
    }
}
