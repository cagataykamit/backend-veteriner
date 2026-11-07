using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.CashCounts;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Reports.Registers;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

public sealed record CreateCashCountBody(
    Guid? ClinicId,
    DateOnly BusinessDate,
    decimal OpeningAmount,
    decimal CountedAmount,
    decimal ExpectedCashAmount,
    string? Note);

public sealed record CorrectCashCountBody(
    decimal OpeningAmount,
    decimal CountedAmount,
    decimal ExpectedCashAmount,
    string? Note,
    string? Reason);

public sealed record VoidCashCountBody(string? Reason);

/// <summary>
/// Günlük kasa kontrolü (TIER-008 ilk dilimi; docs/CASH_COUNT_API_CONTRACT.md). Beklenen nakit gün sonu kasa özetinin nakit satırıdır.
/// Okuma her pakette; kaydetme, düzeltme ve iptal Kurumsal ister (403 <c>Subscriptions.FeatureNotInPlan</c>).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/cash-counts")]
[Produces("application/json")]
[Authorize]
public sealed class CashCountsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public CashCountsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>Günün beklenen nakdi, geçerli sayımı, önceki sayım ve günün düzeltme izi (tek klinik).</summary>
    [HttpGet("day")]
    [Authorize(Policy = PermissionCatalog.CashCounts.Read)]
    [ProducesResponseType(typeof(CashCountDayDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDay([FromQuery] DateOnly date, [FromQuery] Guid? clinicId = null, CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new GetCashCountDayQuery(date, clinicId), ct)).ToActionResult(this);
    }

    /// <summary>Geçmiş sayımlar (tek klinik, en çok 366 gün). Varsayılan yalnız geçerli sayımlar; <c>includeVoided</c> düzeltilenleri de verir.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.CashCounts.Read)]
    [ProducesResponseType(typeof(PagedResult<CashCountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] bool includeVoided = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = RegisterReportConstants.DefaultPageSize,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new GetCashCountsQuery(from, to, clinicId, includeVoided, page, pageSize), ct)).ToActionResult(this);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.CashCounts.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CashCountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCashCountBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var command = new CreateCashCountCommand(
            body.ClinicId, body.BusinessDate, body.OpeningAmount, body.CountedAmount, body.ExpectedCashAmount, body.Note);
        return (await _mediator.Send(command, ct)).ToActionResult(this);
    }

    /// <summary>Eski kayıt gerekçeyle iptal edilir, yerine geçen yeni kayıt döner.</summary>
    [HttpPost("{id:guid}/correct")]
    [Authorize(Policy = PermissionCatalog.CashCounts.Correct)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CashCountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Correct([FromRoute] Guid id, [FromBody] CorrectCashCountBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var command = new CorrectCashCountCommand(id, body.OpeningAmount, body.CountedAmount, body.ExpectedCashAmount, body.Note, body.Reason);
        return (await _mediator.Send(command, ct)).ToActionResult(this);
    }

    [HttpPost("{id:guid}/void")]
    [Authorize(Policy = PermissionCatalog.CashCounts.Correct)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Void([FromRoute] Guid id, [FromBody] VoidCashCountBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new VoidCashCountCommand(id, body.Reason), ct)).ToActionResult(this);
    }
}
