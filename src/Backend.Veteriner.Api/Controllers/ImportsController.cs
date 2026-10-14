using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Imports.Commands.Create;
using Backend.Veteriner.Application.Imports.Commands.Discard;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Queries.GetById;
using Backend.Veteriner.Application.Imports.Queries.GetList;
using Backend.Veteriner.Domain.Imports;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Müşteri / hasta / aşı geçmişi içe aktarma (docs/IMPORT_API_CONTRACT.md). B1: parti kaydı iskeleti;
/// dosya yükleme, eşleme, doğrulama, uygulama ve geri alma sonraki dilimlerde. Kiracı yalnızca <see cref="ITenantContext"/> ile çözülür.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/imports")]
[Produces("application/json")]
[Authorize]
public sealed class ImportsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ImportsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Parti oluşturur (<c>Uploaded</c>). Yeni parti 201; aynı <c>requestId</c> ve aynı içerikle tekrar 200 aynı parti.
    /// Gövde <c>multipart/form-data</c>; dosya alanları B2'de eklenir.
    /// </summary>
    [HttpPost("batches")]
    [Authorize(Policy = PermissionCatalog.Imports.Manage)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ImportBatchDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ImportBatchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateBatch([FromForm] CreateImportBatchForm form, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new CreateImportBatchCommand(form.RequestId ?? Guid.Empty, form.Name, form.ClinicId),
            ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var created = result.Value!;
        if (!created.Created)
            return Ok(created.Batch);

        return CreatedAtAction(
            nameof(GetBatch),
            new { version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0", id = created.Batch.Id },
            created.Batch);
    }

    /// <summary>Kiracının partileri; sıralama oluşturulma zamanı azalan. <c>pageSize</c> 1–100.</summary>
    [HttpGet("batches")]
    [Authorize(Policy = PermissionCatalog.Imports.Read)]
    [ProducesResponseType(typeof(PagedResult<ImportBatchDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetBatches(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] ImportBatchStatus? status = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetImportBatchesListQuery(page, pageSize, status), ct);
        return result.ToActionResult(this);
    }

    [HttpGet("batches/{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Imports.Read)]
    [ProducesResponseType(typeof(ImportBatchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBatch([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetImportBatchByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Taslak partiyi siler (<c>Discarded</c>); ürün verisine dokunmaz. Uygulanmış/çalışan partide 409.</summary>
    [HttpDelete("batches/{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Imports.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DiscardBatch([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new DiscardImportBatchCommand(id), ct);
        return result.ToActionResult(this);
    }
}
