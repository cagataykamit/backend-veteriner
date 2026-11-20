using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.RecordHistory.Contracts.Dtos;
using Backend.Veteriner.Application.RecordHistory.Queries.GetRecordHistory;
using Backend.Veteriner.Application.VitalMeasurements.Commands.Cancel;
using Backend.Veteriner.Application.VitalMeasurements.Commands.Create;
using Backend.Veteriner.Application.VitalMeasurements.Commands.Update;
using Backend.Veteriner.Application.VitalMeasurements.Contracts.Dtos;
using Backend.Veteriner.Application.VitalMeasurements.Queries.GetById;
using Backend.Veteriner.Application.VitalMeasurements.Queries.GetList;
using Backend.Veteriner.Application.VitalMeasurements.Queries.GetSeries;
using Backend.Veteriner.Domain.RecordHistory;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>Vital ölçümleri ve vital eğilimi (HOSP-004); bkz. docs/VITAL_SIGNS_API_CONTRACT.md.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/vital-measurements")]
[Produces("application/json")]
[Authorize]
public sealed class VitalMeasurementsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public VitalMeasurementsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.VitalMeasurements.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateVitalMeasurementCommand cmd, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(cmd, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var id = result.Value;
        return CreatedAtAction(
            nameof(GetById),
            new { version = HttpContext.RequestedApiVersion?.ToString() ?? "1.0", id },
            id);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.VitalMeasurements.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(VitalMeasurementWriteResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateVitalMeasurementBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (body.Id is { } bid && bid != Guid.Empty && bid != id)
            return Result.Failure("VitalMeasurements.RouteIdMismatch", "Adresteki kayıt ile gönderilen kayıt eşleşmiyor. Sayfayı yenileyip tekrar deneyin.").ToActionResult(this);

        var result = await _mediator.Send(
            new UpdateVitalMeasurementCommand(
                id,
                body.MeasuredAtUtc,
                body.TemperatureC,
                body.HeartRateBpm,
                body.RespiratoryRatePerMin,
                body.WeightKg,
                body.Note,
                body.RowVersion),
            ct);
        return result.ToActionResult(this);
    }

    /// <summary>Ölçümü gerekçeyle iptal eder (silmez; ADR-043 K4).</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PermissionCatalog.VitalMeasurements.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(VitalMeasurementWriteResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel([FromRoute] Guid id, [FromBody] CancelVitalMeasurementBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new CancelVitalMeasurementCommand(id, body.Reason, body.RowVersion), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Ölçümün değişiklik geçmişi (RECORD-002), yeniden eskiye; bkz. docs/RECORD_HISTORY_API_CONTRACT.md.</summary>
    [HttpGet("{id:guid}/history")]
    [Authorize(Policy = PermissionCatalog.VitalMeasurements.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<RecordChangeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetHistory([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetRecordHistoryQuery(RecordHistoryTypes.VitalMeasurement, id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Vital eğilimi: vital ölçümleri ve muayene vitalleri birleşik, artan zaman sırasıyla; her nokta <c>source</c> taşır.
    /// <c>hospitalizationId</c> verilirse pencere yatış süresidir.
    /// </summary>
    [HttpGet("series")]
    [Authorize(Policy = PermissionCatalog.VitalMeasurements.Read)]
    [ProducesResponseType(typeof(VitalSeriesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSeries(
        [FromQuery] Guid petId,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? hospitalizationId = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] int? limit = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetVitalSeriesQuery(petId, clinicId, hospitalizationId, fromUtc, toUtc, limit), ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.VitalMeasurements.Read)]
    [ProducesResponseType(typeof(VitalMeasurementDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetVitalMeasurementByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Hastanın vital ölçümleri (<c>petId</c> zorunlu), en yeni üstte; iptal edilenler <c>includeCancelled=true</c> ile gelir.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.VitalMeasurements.Read)]
    [ProducesResponseType(typeof(PagedResult<VitalMeasurementListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] PageRequest page,
        [FromQuery] Guid petId,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? hospitalizationId = null,
        [FromQuery] Guid? examinationId = null,
        [FromQuery] DateTime? dateFromUtc = null,
        [FromQuery] DateTime? dateToUtc = null,
        [FromQuery] bool includeCancelled = false,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var paging = PageRequestQuery.BindFromQuery(Request.Query, page);
        var result = await _mediator.Send(
            new GetVitalMeasurementsListQuery(
                paging,
                petId,
                clinicId,
                hospitalizationId,
                examinationId,
                dateFromUtc,
                dateToUtc,
                includeCancelled),
            ct);
        return result.ToActionResult(this);
    }
}
