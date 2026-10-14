using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Imports.Commands.Commit;
using Backend.Veteriner.Application.Imports.Commands.Create;
using Backend.Veteriner.Application.Imports.Commands.Discard;
using Backend.Veteriner.Application.Imports.Commands.ReleaseReminders;
using Backend.Veteriner.Application.Imports.Commands.Rollback;
using Backend.Veteriner.Application.Imports.Commands.UpdateMapping;
using Backend.Veteriner.Application.Imports.Commands.Validate;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Queries.GetById;
using Backend.Veteriner.Application.Imports.Queries.GetErrors;
using Backend.Veteriner.Application.Imports.Queries.GetList;
using Backend.Veteriner.Application.Imports.Queries.GetPreview;
using Backend.Veteriner.Application.Imports.Queries.GetProgress;
using Backend.Veteriner.Application.Imports.Queries.GetRollbackPreview;
using Backend.Veteriner.Application.Imports.Queries.GetRows;
using Backend.Veteriner.Application.Imports.Queries.GetTemplate;
using Backend.Veteriner.Application.Imports.Reading;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Müşteri / hasta / aşı geçmişi içe aktarma (docs/IMPORT_API_CONTRACT.md). B1: parti kaydı; B2: dosya okuma, şablon, eşleme;
/// B3: doğrulama (kuru çalıştırma), önizleme, satırlar, hata raporu; B4: uygulama, ilerleme, hatırlatma serbest bırakma;
/// B5: geri alma önizlemesi ve geri alma. Kiracı yalnızca <see cref="ITenantContext"/> ile çözülür.
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
    private readonly ImportFileLimits _limits;

    public ImportsController(IMediator mediator, ITenantContext tenantContext, IOptions<ImportFileLimits> limits)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
        _limits = limits.Value;
    }

    /// <summary>
    /// Dosyayı okuyup parti oluşturur (<c>Uploaded</c>). Yeni parti 201; aynı <c>requestId</c> ve aynı içerikle tekrar 200
    /// aynı parti. Gövde <c>multipart/form-data</c>: <c>file</c> (XLSX) veya <c>customersFile</c>/<c>petsFile</c>/<c>vaccinationsFile</c> (CSV).
    /// </summary>
    [HttpPost("batches")]
    [Authorize(Policy = PermissionCatalog.Imports.Manage)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ImportFileLimits.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ImportFileLimits.MaxRequestBytes)]
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

        // Aşırı büyük dosya belleğe alınmadan reddedilir; kalan denetimler okuyucudadır.
        if (new[] { form.File, form.CustomersFile, form.PetsFile, form.VaccinationsFile }
            .Any(f => f is not null && f.Length > _limits.MaxFileBytes))
        {
            return Result.Failure(
                    "Imports.FileTooLarge",
                    $"Dosya en fazla {_limits.MaxFileBytes / (1024 * 1024)} MB olabilir.")
                .ToActionResult(this);
        }

        var files = new ImportFileSet(
            await ReadAsync(form.File, ct),
            await ReadAsync(form.CustomersFile, ct),
            await ReadAsync(form.PetsFile, ct),
            await ReadAsync(form.VaccinationsFile, ct));

        var result = await _mediator.Send(
            new CreateImportBatchCommand(
                form.RequestId ?? Guid.Empty, form.Name, form.ClinicId, files, form.Encoding, form.Delimiter),
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

    /// <summary>
    /// Sütun ve değer eşlemesini kaydeder (tam değiştirme); partiyi <c>Mapped</c> yapar. Eksik zorunlu alan, bilinmeyen
    /// sütun veya geçersiz değer hedefinde 400; uygun olmayan durumda 409.
    /// </summary>
    [HttpPut("batches/{id:guid}/mapping")]
    [Authorize(Policy = PermissionCatalog.Imports.Manage)]
    [ProducesResponseType(typeof(ImportBatchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateMapping(
        [FromRoute] Guid id,
        [FromBody] ImportMappingRequest body,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new UpdateImportBatchMappingCommand(id, body), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Doldurulacak şablon: <c>format=xlsx</c> (Musteriler, Hayvanlar, Asilar, Aciklama sayfaları) veya
    /// <c>format=csv&amp;table=customers|pets|vaccinations</c>.
    /// </summary>
    [HttpGet("template")]
    [Authorize(Policy = PermissionCatalog.Imports.Read)]
    [Produces("application/octet-stream")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetTemplate(
        [FromQuery] string? format,
        [FromQuery] string? table,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetImportTemplateQuery(format, table), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        Response.Headers.CacheControl = "no-store";
        var file = result.Value!;
        return File(file.Content, file.ContentType, file.FileName);
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

    /// <summary>
    /// Kuru çalıştırmayı arka planda başlatır (satır kuralları, mükerrer/çip/bağlantı; müşteri/hasta/aşı kaydı oluşmaz):
    /// yeni iş 202 + parti (<c>Validating</c>), iş zaten kuyrukta/sürüyorsa 200 + aynı parti. Sonuç
    /// <c>GET .../progress</c> yoklanarak beklenir (<c>Validated</c> | <c>ValidationFailed</c>).
    /// </summary>
    [HttpPost("batches/{id:guid}/validate")]
    [Authorize(Policy = PermissionCatalog.Imports.Manage)]
    [ProducesResponseType(typeof(ImportBatchDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ImportBatchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ValidateBatch([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new ValidateImportBatchCommand(id), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var validation = result.Value!;
        return validation.Started ? Accepted(validation.Batch) : Ok(validation.Batch);
    }

    /// <summary>Kuru çalıştırma önizlemesi: sayaçlar, eşlenemeyen değerler, hatırlatma etkisi, engeller.</summary>
    [HttpGet("batches/{id:guid}/preview")]
    [Authorize(Policy = PermissionCatalog.Imports.Read)]
    [ProducesResponseType(typeof(ImportPreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetPreview([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetImportBatchPreviewQuery(id), ct);
        if (result.IsSuccess)
            Response.Headers.CacheControl = "no-store";
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Satır sonuçları (süzgeç: <c>table</c>, <c>status</c>, <c>code</c>; sayfalı). Kişisel veri içerir, önbelleğe alınmaz.
    /// </summary>
    [HttpGet("batches/{id:guid}/rows")]
    [Authorize(Policy = PermissionCatalog.Imports.Read)]
    [ProducesResponseType(typeof(PagedResult<ImportRowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    public async Task<IActionResult> GetRows(
        [FromRoute] Guid id,
        [FromQuery] string? table = null,
        [FromQuery] string? status = null,
        [FromQuery] string? code = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = GetImportBatchRowsQuery.DefaultPageSize,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetImportBatchRowsQuery(id, table, status, code, page, pageSize), ct);
        if (result.IsSuccess)
            Response.Headers.CacheControl = "no-store";
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Hata raporu dosyası (<c>format=xlsx|csv</c>, varsayılan xlsx): hatalı satırların orijinal değerleri + hata kodu ve
    /// açıklama. Formül enjeksiyonuna karşı hücre değerleri güvenli yazılır; indirme audit'e yazılır.
    /// </summary>
    [HttpGet("batches/{id:guid}/errors")]
    [Authorize(Policy = PermissionCatalog.Imports.Read)]
    [Produces("application/octet-stream")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    public async Task<IActionResult> GetErrors([FromRoute] Guid id, [FromQuery] string? format, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetImportBatchErrorsQuery(id, format), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        Response.Headers.CacheControl = "no-store";
        var file = result.Value!;
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>
    /// Önizlenen doğrulamayı onaylar ve arka plan uygulamasını başlatır: yeni iş 202 + parti (<c>Committing</c>), aynı
    /// <c>validationId</c> ile tekrar 200 + aynı parti. İlerleme <c>GET .../progress</c> ile yoklanır.
    /// </summary>
    [HttpPost("batches/{id:guid}/commit")]
    [Authorize(Policy = PermissionCatalog.Imports.Commit)]
    [ProducesResponseType(typeof(ImportBatchDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ImportBatchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CommitBatch([FromRoute] Guid id, [FromBody] ImportCommitBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new CommitImportBatchCommand(id, body.ValidationId, body.OnErrors), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var commit = result.Value!;
        return commit.Started ? Accepted(commit.Batch) : Ok(commit.Batch);
    }

    /// <summary>Doğrulama, uygulama ve geri alma ilerlemesi (hafif yoklama ucu; önerilen aralık 2 sn).</summary>
    [HttpGet("batches/{id:guid}/progress")]
    [Authorize(Policy = PermissionCatalog.Imports.Read)]
    [ProducesResponseType(typeof(ImportProgressDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProgress([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetImportBatchProgressQuery(id), ct);
        if (result.IsSuccess)
            Response.Headers.CacheControl = "no-store";
        return result.ToActionResult(this);
    }

    /// <summary>
    /// İçe aktarılan aşıların hatırlatmalarını bilinçli açar (geri alınamaz). <c>confirmEmailCount</c> güncel
    /// hatırlatma penceresi e-posta sayısıyla eşleşmezse 409 + güncel sayı.
    /// </summary>
    [HttpPost("batches/{id:guid}/release-reminders")]
    [Authorize(Policy = PermissionCatalog.Imports.Commit)]
    [ProducesResponseType(typeof(ImportReleaseRemindersResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReleaseReminders(
        [FromRoute] Guid id,
        [FromBody] ImportReleaseRemindersBody body,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new ReleaseImportRemindersCommand(id, body.ConfirmEmailCount), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Geri alma önizlemesi: partinin oluşturduğu kayıtlardan silinebilecek ve bağı olduğu için kalacak olanlar (neden
    /// kodlarıyla). Hiçbir şey yazmaz; kayıt kimlikleri döndüğü için <c>Imports.Commit</c> ister.
    /// </summary>
    [HttpGet("batches/{id:guid}/rollback-preview")]
    [Authorize(Policy = PermissionCatalog.Imports.Commit)]
    [ProducesResponseType(typeof(ImportRollbackPreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetRollbackPreview([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetImportRollbackPreviewQuery(id), ct);
        if (result.IsSuccess)
            Response.Headers.CacheControl = "no-store";
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Geri almayı arka planda başlatır: yeni iş 202 + parti (<c>RollingBack</c>), aynı <c>requestId</c> ile tekrar 200 +
    /// parti. <c>Fail</c> politikasında bağı olan kayıt varsa 409 <c>Imports.RollbackBlocked</c> (hiçbir şey silinmez).
    /// </summary>
    [HttpPost("batches/{id:guid}/rollback")]
    [Authorize(Policy = PermissionCatalog.Imports.Commit)]
    [ProducesResponseType(typeof(ImportBatchDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ImportBatchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RollbackBatch([FromRoute] Guid id, [FromBody] ImportRollbackBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new RollbackImportBatchCommand(id, body.RequestId, body.Reason, body.BlockedPolicy), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var rollback = result.Value!;
        return rollback.Started ? Accepted(rollback.Batch) : Ok(rollback.Batch);
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

    private static async Task<ImportUploadedFile?> ReadAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null)
            return null;

        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream(capacity: (int)file.Length);
        await stream.CopyToAsync(buffer, ct);
        return new ImportUploadedFile(file.FileName, file.ContentType, buffer.ToArray());
    }
}
