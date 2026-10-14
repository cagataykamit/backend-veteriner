using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Vaccinations.Commands.Create;
using Backend.Veteriner.Application.Vaccinations.Commands.Update;
using Backend.Veteriner.Application.Vaccinations.Contracts.Dtos;
using Backend.Veteriner.Application.Vaccinations.Queries.GetById;
using Backend.Veteriner.Application.Vaccinations.Queries.GetList;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Pet bazlı aşı kayıtları. Kiracı yalnızca <see cref="ITenantContext"/> ile çözülür.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/vaccinations")]
[Produces("application/json")]
[Authorize]
public sealed class VaccinationsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;
    private readonly IAuthorizationService _authorization;

    public VaccinationsController(IMediator mediator, ITenantContext tenantContext, IAuthorizationService authorization)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
        _authorization = authorization;
    }

    /// <summary>
    /// Aşı kaydı oluşturur. <c>productId</c> yoksa 201 + çıplak <c>id</c>. <c>productId</c> (ve opsiyonel <c>quantity</c>, <c>batchId</c>, <c>requestId</c>) verilirse
    /// aynı işlemde stoktan düşen uygulama da yazılır (ayrıca <c>Administrations.Create</c> gerekir); yanıt <see cref="VaccinationCreateResultDto"/>, aynı <c>requestId</c> tekrarında 200.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Vaccinations.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(VaccinationCreateResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(VaccinationCreateResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateVaccinationCommand cmd, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        // Stok düşümü uygulama iznindedir: aşı iznine ek olarak aranır.
        if (cmd.ProductId.HasValue
            && !(await _authorization.AuthorizeAsync(User, PermissionCatalog.Administrations.Create)).Succeeded)
        {
            return Result.Failure(
                "Vaccinations.AdministrationForbidden",
                "Ürünle (stok düşümlü) aşı kaydı için uygulama kaydı yetkisi gerekir.").ToActionResult(this);
        }

        var result = await _mediator.Send(cmd, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var created = result.Value!;
        if (cmd.ProductId is null)
        {
            return CreatedAtAction(
                nameof(GetById),
                new { version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0", id = created.Id },
                created.Id);
        }

        if (!created.Created)
            return Ok(created);

        return CreatedAtAction(
            nameof(GetById),
            new { version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0", id = created.Id },
            created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Vaccinations.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateVaccinationBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (body.Id is { } bid && bid != Guid.Empty && bid != id)
            return Result.Failure("Vaccinations.RouteIdMismatch", "Route id ile body id uyusmuyor.").ToActionResult(this);

        var cmd = new UpdateVaccinationCommand(
            id,
            body.ClinicId,
            body.PetId,
            body.ExaminationId,
            body.VaccineDefinitionId,
            body.Status,
            body.AppliedAtUtc,
            body.DueAtUtc,
            body.Notes);

        var result = await _mediator.Send(cmd, ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Vaccinations.Read)]
    [ProducesResponseType(typeof(VaccinationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetVaccinationByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Sayfalı aşı listesi. <c>search</c> (veya <c>page.search</c>): aşı adı, notlar; müşteri metin alanlarında; hayvan ad/tür/ırk (hayvan listesi ile aynı küme). <c>sort</c>/<c>order</c> işlenmez. <c>onlyOverdue</c>: planlı ve vadesi geçmiş aşılar (Scheduled, DueAtUtc dolu ve &lt; UTC şimdi); açıkken istek <c>status</c> değerini yok sayar. <c>examinationId</c>: yalnız bu muayeneye bağlı aşılar (kiracı/klinik kapsamı yine uygulanır).</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Vaccinations.Read)]
    [ProducesResponseType(typeof(PagedResult<VaccinationListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetList(
        [FromQuery] PageRequest page,
        [FromQuery] string? search = null,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] VaccinationStatus? status = null,
        [FromQuery] DateTime? dueFromUtc = null,
        [FromQuery] DateTime? dueToUtc = null,
        [FromQuery] DateTime? appliedFromUtc = null,
        [FromQuery] DateTime? appliedToUtc = null,
        [FromQuery] bool onlyOverdue = false,
        [FromQuery] Guid? examinationId = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var paging = PageRequestQuery.BindFromQuery(Request.Query, page);
        var result = await _mediator.Send(
            new GetVaccinationsListQuery(
                PageRequestQuery.WithMergedSearch(paging, search),
                clinicId,
                petId,
                status,
                dueFromUtc,
                dueToUtc,
                appliedFromUtc,
                appliedToUtc,
                onlyOverdue,
                examinationId),
            ct);
        return result.ToActionResult(this);
    }
}
