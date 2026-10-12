using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Administrations.Commands.Create;
using Backend.Veteriner.Application.Administrations.Commands.Void;
using Backend.Veteriner.Application.Administrations.Contracts.Dtos;
using Backend.Veteriner.Application.Administrations.Queries.GetById;
using Backend.Veteriner.Application.Administrations.Queries.GetList;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Hastaya klinikte uygulanan ürün kayıtları; oluşturma stoğu, iptal stok iadesini aynı transaction'da yapar.
/// Reçeteden ayrıdır. Kiracı yalnızca <see cref="ITenantContext"/> ile çözülür.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/administrations")]
[Produces("application/json")]
[Authorize]
public sealed class AdministrationsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public AdministrationsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Uygulamayı kaydeder ve ürün stoğunu düşer. Yeni kayıt 201; aynı <c>requestId</c> ile tekrar istek 200 ve <c>created: false</c>.
    /// Yetersiz stok kaydı engeller; düşük stok yalnız yanıtta bildirilir.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Administrations.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AdministrationCreateResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(AdministrationCreateResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateAdministrationBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new CreateAdministrationCommand(
                body.ClinicId,
                body.PetId,
                body.ExaminationId,
                body.TreatmentId,
                body.ProductId,
                body.Quantity,
                body.AdministeredAtUtc,
                body.Notes,
                body.RequestId),
            ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var created = result.Value!;
        if (!created.Created)
            return Ok(created);

        return CreatedAtAction(
            nameof(GetById),
            new { version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0", id = created.Administration.Id },
            created);
    }

    /// <summary>
    /// Uygulamayı iptal eder (silmez) ve stoğu ters hareketle geri verir. Gerekçe zorunludur (5-500 karakter).
    /// Kesinleşmiş muayeneye bağlı uygulama da iptal edilebilir.
    /// </summary>
    [HttpPost("{id:guid}/void")]
    [Authorize(Policy = PermissionCatalog.Administrations.Correct)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AdministrationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Void([FromRoute] Guid id, [FromBody] VoidRecordBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new VoidAdministrationCommand(id, body.Reason), ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Administrations.Read)]
    [ProducesResponseType(typeof(AdministrationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetAdministrationByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Klinik kapsamı zorunlu: <c>clinicId</c> veya aktif klinik bağlamı. Sıralama: uygulama zamanı azalan; iptaller varsayılan gizli.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Administrations.Read)]
    [ProducesResponseType(typeof(PagedResult<AdministrationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] Guid? examinationId = null,
        [FromQuery] Guid? treatmentId = null,
        [FromQuery] Guid? productId = null,
        [FromQuery] bool includeVoided = false,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new GetAdministrationsListQuery(page, pageSize, clinicId, petId, examinationId, treatmentId, productId, includeVoided),
            ct);
        return result.ToActionResult(this);
    }
}
