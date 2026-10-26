using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Files.Commands.Upload;
using Backend.Veteriner.Application.Files.Contracts.Dtos;
using Backend.Veteriner.Application.Files.Queries.CreateAccessUrl;
using Backend.Veteriner.Application.Files.Queries.GetContent;
using Backend.Veteriner.Application.Imaging.Commands.Cancel;
using Backend.Veteriner.Application.Imaging.Commands.Create;
using Backend.Veteriner.Application.Imaging.Commands.Update;
using Backend.Veteriner.Application.Imaging.Contracts.Dtos;
using Backend.Veteriner.Application.Imaging.Queries.GetById;
using Backend.Veteriner.Application.Imaging.Queries.GetList;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Imaging;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>Görüntüleme kayıtları ve dosyaları (docs/IMAGING_API_CONTRACT.md, ADR-007).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/imaging-records")]
[Produces("application/json")]
[Authorize]
public sealed class ImagingRecordsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ImagingRecordsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Imaging.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] CreateImagingRecordBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new CreateImagingRecordCommand(
                body.ClinicId,
                body.PetId,
                body.ExaminationId,
                body.PerformedAtUtc,
                body.Modality,
                body.Description,
                body.ClinicalNote),
            ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var id = result.Value;
        return CreatedAtAction(
            nameof(GetById),
            new { version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0", id },
            id);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Imaging.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ImagingRecordWriteResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateImagingRecordBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (body.Id is { } bid && bid != Guid.Empty && bid != id)
            return Result.Failure("ImagingRecords.RouteIdMismatch", "Route id ile body id uyusmuyor.").ToActionResult(this);

        var result = await _mediator.Send(
            new UpdateImagingRecordCommand(
                id,
                body.ExaminationId,
                body.PerformedAtUtc,
                body.Modality,
                body.Description,
                body.ClinicalNote,
                body.RowVersion),
            ct);
        return result.ToActionResult(this);
    }

    /// <summary>Gerekçeli iptal (silmez); dosyaların erişimi kapanır.</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PermissionCatalog.Imaging.Cancel)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ImagingRecordWriteResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel([FromRoute] Guid id, [FromBody] CancelImagingRecordBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new CancelImagingRecordCommand(id, body.Reason, body.RowVersion), ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Imaging.Read)]
    [ProducesResponseType(typeof(ImagingRecordDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetImagingRecordByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Sayfalı liste. <c>search</c>: açıklama, klinik not, hasta/müşteri adı. <c>status</c> verilmezse yalnız etkin kayıtlar.
    /// Sıralama: <c>sort=performedAtUtc</c>, <c>order=asc|desc</c>; varsayılan yeniden eskiye.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Imaging.Read)]
    [ProducesResponseType(typeof(PagedResult<ImagingRecordListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] PageRequest page,
        [FromQuery] string? search = null,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] Guid? examinationId = null,
        [FromQuery] ImagingModality? modality = null,
        [FromQuery] ImagingRecordStatus? status = null,
        [FromQuery] DateTime? dateFromUtc = null,
        [FromQuery] DateTime? dateToUtc = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var paging = PageRequestQuery.BindFromQuery(Request.Query, page);
        var result = await _mediator.Send(
            new GetImagingRecordsListQuery(
                PageRequestQuery.WithMergedSearch(paging, search),
                clinicId,
                petId,
                examinationId,
                modality,
                status,
                dateFromUtc,
                dateToUtc),
            ct);
        return result.ToActionResult(this);
    }

    /// <summary>Tek dosya yükler (<c>multipart/form-data</c>, alan <c>file</c>); tür içerikten belirlenir.</summary>
    [HttpPost("{id:guid}/files")]
    [Authorize(Policy = PermissionCatalog.Imaging.Create)]
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
            new UploadStoredFileCommand(StoredFileOwnerType.ImagingRecord, id, form.File.FileName, form.File.Length, content),
            ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult(this);
    }

    /// <summary>Dosya akışı (Bearer). <c>download=true</c> → <c>attachment</c>.</summary>
    [HttpGet("{id:guid}/files/{fileId:guid}/content")]
    [Authorize(Policy = PermissionCatalog.Imaging.Read)]
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

        var result = await _mediator.Send(new GetStoredFileContentQuery(StoredFileOwnerType.ImagingRecord, id, fileId), ct);
        return result.ToFileResult(this, download);
    }

    /// <summary><c>&lt;img src&gt;</c> için kısa ömürlü imzalı URL.</summary>
    [HttpPost("{id:guid}/files/{fileId:guid}/access-url")]
    [Authorize(Policy = PermissionCatalog.Imaging.Read)]
    [ProducesResponseType(typeof(StoredFileAccessUrlDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateFileAccessUrl([FromRoute] Guid id, [FromRoute] Guid fileId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new CreateStoredFileAccessUrlQuery(StoredFileOwnerType.ImagingRecord, id, fileId), ct);
        return result.ToActionResult(this);
    }
}
