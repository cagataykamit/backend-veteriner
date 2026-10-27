using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Files.Commands.Upload;
using Backend.Veteriner.Application.Files.Contracts.Dtos;
using Backend.Veteriner.Application.Files.Queries.CreateAccessUrl;
using Backend.Veteriner.Application.Files.Queries.GetContent;
using Backend.Veteriner.Application.Files.Queries.GetList;
using Backend.Veteriner.Application.LabResults.Commands.Cancel;
using Backend.Veteriner.Application.LabResults.Commands.Create;
using Backend.Veteriner.Application.LabResults.Commands.Update;
using Backend.Veteriner.Application.LabResults.Contracts.Dtos;
using Backend.Veteriner.Application.LabResults.Queries.GetById;
using Backend.Veteriner.Application.LabResults.Queries.GetList;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>Clinical laboratory results for pets (optional link to examination).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/lab-results")]
[Produces("application/json")]
[Authorize]
public sealed class LabResultsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public LabResultsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.LabResults.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] CreateLabResultCommand cmd, CancellationToken ct)
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
    [Authorize(Policy = PermissionCatalog.LabResults.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(LabResultWriteResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateLabResultBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (body.Id is { } bid && bid != Guid.Empty && bid != id)
            return Result.Failure("LabResults.RouteIdMismatch", "Route id ile body id uyusmuyor.").ToActionResult(this);

        var cmd = new UpdateLabResultCommand(
            id,
            body.ClinicId,
            body.PetId,
            body.ExaminationId,
            body.ResultDateUtc,
            body.TestName,
            body.ResultText,
            body.Interpretation,
            body.Notes,
            body.RowVersion,
            body.VisitId);

        var result = await _mediator.Send(cmd, ct);
        return result.ToActionResult(this);
    }

    /// <summary>Lab kaydını gerekçeyle iptal eder (silmez); iptal kaydı değiştirilemez.</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PermissionCatalog.LabResults.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(LabResultWriteResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel([FromRoute] Guid id, [FromBody] CancelLabResultBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new CancelLabResultCommand(id, body.Reason, body.RowVersion), ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.LabResults.Read)]
    [ProducesResponseType(typeof(LabResultDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetLabResultByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Paged list. <c>search</c> (or <c>page.search</c>): testName, resultText, interpretation, notes, and pet/client name match (same pattern as prescriptions/treatments).
    /// Sorting: <c>sort=requestedAtUtc</c>, <c>order=asc|desc</c>; other sort values return 400. Without sort: newest first.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.LabResults.Read)]
    [ProducesResponseType(typeof(PagedResult<LabResultListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] PageRequest page,
        [FromQuery] string? search = null,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] DateTime? dateFromUtc = null,
        [FromQuery] DateTime? dateToUtc = null,
        [FromQuery] LabResultStatus? status = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var paging = PageRequestQuery.BindFromQuery(Request.Query, page);
        var result = await _mediator.Send(
            new GetLabResultsListQuery(
                PageRequestQuery.WithMergedSearch(paging, search),
                clinicId,
                petId,
                dateFromUtc,
                dateToUtc,
                status),
            ct);
        return result.ToActionResult(this);
    }

    /// <summary>Lab sonucunun hazır dosyaları (PDF/fotoğraf), yükleme sırasına göre (docs/IMAGING_API_CONTRACT.md §5).</summary>
    [HttpGet("{id:guid}/files")]
    [Authorize(Policy = PermissionCatalog.LabResults.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<StoredFileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFiles([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetStoredFilesQuery(StoredFileOwnerType.LabResult, id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Lab sonucuna tek dosya yükler (<c>multipart/form-data</c>, alan <c>file</c>); iptal edilmiş kayda eklenemez.</summary>
    [HttpPost("{id:guid}/files")]
    [Authorize(Policy = PermissionCatalog.LabResults.Update)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(StoredFileHttpResults.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = StoredFileHttpResults.MaxRequestBytes)]
    [ProducesResponseType(typeof(StoredFileDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> UploadFile([FromRoute] Guid id, [FromForm] StoredFileUploadForm form, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var valid = StoredFileHttpResults.ValidateUpload(form.File);
        if (!valid.IsSuccess)
            return valid.ToActionResult(this);

        await using var content = form.File!.OpenReadStream();
        var result = await _mediator.Send(
            new UploadStoredFileCommand(StoredFileOwnerType.LabResult, id, form.File.FileName, form.File.Length, content),
            ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult(this);
    }

    /// <summary>Dosya akışı (Bearer). <c>download=true</c> → <c>attachment</c>.</summary>
    [HttpGet("{id:guid}/files/{fileId:guid}/content")]
    [Authorize(Policy = PermissionCatalog.LabResults.Read)]
    [Produces("application/octet-stream", "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFileContent(
        [FromRoute] Guid id,
        [FromRoute] Guid fileId,
        [FromQuery] bool download = false,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetStoredFileContentQuery(StoredFileOwnerType.LabResult, id, fileId), ct);
        return result.ToFileResult(this, download);
    }

    /// <summary><c>&lt;img src&gt;</c> için kısa ömürlü imzalı URL.</summary>
    [HttpPost("{id:guid}/files/{fileId:guid}/access-url")]
    [Authorize(Policy = PermissionCatalog.LabResults.Read)]
    [ProducesResponseType(typeof(StoredFileAccessUrlDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateFileAccessUrl([FromRoute] Guid id, [FromRoute] Guid fileId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new CreateStoredFileAccessUrlQuery(StoredFileOwnerType.LabResult, id, fileId), ct);
        return result.ToActionResult(this);
    }
}
