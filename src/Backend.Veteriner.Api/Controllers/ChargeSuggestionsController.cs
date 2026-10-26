using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Charges.Contracts.Dtos;
using Backend.Veteriner.Application.ChargeSuggestions.Commands.Convert;
using Backend.Veteriner.Application.ChargeSuggestions.Commands.Waive;
using Backend.Veteriner.Application.ChargeSuggestions.Contracts.Dtos;
using Backend.Veteriner.Application.ChargeSuggestions.Queries.GetList;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// "Ücret bekliyor" kalemleri (ADR-018): hekimin işleminden doğar, resepsiyon ücrete çevirir veya ücretsiz sayar.
/// Okuma <c>Charges.Read</c> (hekim dahil), karar <c>Charges.Create</c> (hekimde yok, ADR-012 K2).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/charge-suggestions")]
[Produces("application/json")]
[Authorize]
public sealed class ChargeSuggestionsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ChargeSuggestionsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>Bekleyen kalemler. Klinik kapsamı zorunlu: <c>clinicId</c> veya aktif klinik bağlamı. Sıralama: işlem zamanı azalan.
    /// <c>clientId</c>: müşterinin güncel hayvanlarının kalemleri (müşteri kartı tek istekle okur).</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Charges.Read)]
    [ProducesResponseType(typeof(PagedResult<ChargeSuggestionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] Guid? examinationId = null,
        [FromQuery] Guid? visitId = null,
        [FromQuery] Guid? clientId = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new GetChargeSuggestionsQuery(page, pageSize, clinicId, petId, examinationId, visitId, clientId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Kalemi ücrete çevirir. Alanlar verilmezse kalemdeki değerler kullanılır. Yeni ücret 201; aynı <c>requestId</c> ile tekrar 200 ve <c>created: false</c>.
    /// </summary>
    [HttpPost("{id:guid}/convert")]
    [Authorize(Policy = PermissionCatalog.Charges.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ChargeCreateResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ChargeCreateResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Convert(
        [FromRoute] Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ConvertChargeSuggestionBody? body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new ConvertChargeSuggestionCommand(id, body?.Description, body?.Quantity, body?.UnitPrice, body?.RequestId), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var created = result.Value!;
        if (!created.Created)
            return Ok(created);

        return CreatedAtAction(
            nameof(ChargesController.GetById),
            "Charges",
            new { version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0", id = created.Charge.Id },
            created);
    }

    /// <summary>Kalemi gerekçeyle ücretsiz sayar (5-500 karakter); geri alınmaz.</summary>
    [HttpPost("{id:guid}/waive")]
    [Authorize(Policy = PermissionCatalog.Charges.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ChargeSuggestionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Waive([FromRoute] Guid id, [FromBody] VoidRecordBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new WaiveChargeSuggestionCommand(id, body.Reason), ct);
        return result.ToActionResult(this);
    }
}
