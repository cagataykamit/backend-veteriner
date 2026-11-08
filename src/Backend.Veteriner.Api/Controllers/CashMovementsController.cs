using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.CashRegisters;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Reports.Registers;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

public sealed record CreateCashMovementBody(
    Guid? ClinicId,
    Guid CashRegisterId,
    CashMovementDirection Direction,
    DateOnly BusinessDate,
    decimal Amount,
    Guid? CategoryId,
    string? Description);

public sealed record CreateCashTransferBody(
    Guid? ClinicId,
    Guid SourceCashRegisterId,
    Guid TargetCashRegisterId,
    DateOnly BusinessDate,
    decimal Amount,
    string? Description);

public sealed record VoidCashMovementBody(string? Reason);

public sealed record CashMovementCategoryBody(string? Name, bool IsActive = true);

/// <summary>
/// Kasa giriş/çıkışı, kasalar arası aktarım ve gelir/gider türleri (TIER-008; docs/CASH_REGISTERS_API_CONTRACT.md). Okuma her pakette;
/// yazma Profesyonel ve Kurumsal ister. Düzeltme kaydı ezmez: gerekçeli iptal ve yeni kayıt.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/cash-movements")]
[Produces("application/json")]
[Authorize]
public sealed class CashMovementsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public CashMovementsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>Giriş/çıkış ve aktarım bacakları (tek klinik, en çok 366 gün). Varsayılan iptal edilenler hariç.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.CashRegisters.Read)]
    [ProducesResponseType(typeof(PagedResult<CashMovementDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? cashRegisterId = null,
        [FromQuery] bool includeVoided = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = RegisterReportConstants.DefaultPageSize,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var query = new GetCashMovementsQuery(from, to, clinicId, cashRegisterId, includeVoided, page, pageSize);
        return (await _mediator.Send(query, ct)).ToActionResult(this);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.CashMovements.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CashMovementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCashMovementBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var command = new CreateCashMovementCommand(
            body.ClinicId, body.CashRegisterId, body.Direction, body.BusinessDate, body.Amount, body.CategoryId, body.Description);
        return (await _mediator.Send(command, ct)).ToActionResult(this);
    }

    /// <summary>Kasalar arası aktarım: tek işlemde kaynaktan çıkış ve hedefe giriş; çıkış bacağı döner.</summary>
    [HttpPost("transfers")]
    [Authorize(Policy = PermissionCatalog.CashMovements.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CashMovementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateTransfer([FromBody] CreateCashTransferBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var command = new CreateCashTransferCommand(
            body.ClinicId, body.SourceCashRegisterId, body.TargetCashRegisterId, body.BusinessDate, body.Amount, body.Description);
        return (await _mediator.Send(command, ct)).ToActionResult(this);
    }

    /// <summary>Gerekçeli iptal; aktarımda iki bacak birlikte iptal edilir.</summary>
    [HttpPost("{id:guid}/void")]
    [Authorize(Policy = PermissionCatalog.CashMovements.Void)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Void([FromRoute] Guid id, [FromBody] VoidCashMovementBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new VoidCashMovementCommand(id, body.Reason), ct)).ToActionResult(this);
    }

    /// <summary>Kiracının gelir/gider türleri, ada göre. Varsayılan yalnız aktifler.</summary>
    [HttpGet("categories")]
    [Authorize(Policy = PermissionCatalog.CashRegisters.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<CashMovementCategoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategories([FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new GetCashMovementCategoriesQuery(includeInactive), ct)).ToActionResult(this);
    }

    [HttpPost("categories")]
    [Authorize(Policy = PermissionCatalog.CashRegisters.Manage)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CashMovementCategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCategory([FromBody] CashMovementCategoryBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new CreateCashMovementCategoryCommand(body.Name), ct)).ToActionResult(this);
    }

    [HttpPut("categories/{id:guid}")]
    [Authorize(Policy = PermissionCatalog.CashRegisters.Manage)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CashMovementCategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateCategory([FromRoute] Guid id, [FromBody] CashMovementCategoryBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new UpdateCashMovementCategoryCommand(id, body.Name, body.IsActive), ct)).ToActionResult(this);
    }
}
