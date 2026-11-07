using System.Text.Json;
using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.ClinicSites;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.ClinicSites;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Kliniğin web sitesi yönetimi (ADR-045; docs/CLINIC_SITE_API_CONTRACT.md §2–5). Ayarlar mevcut klinik ayar yetkisiyle:
/// okuma <c>Clinics.Read</c>, yazma <c>Clinics.Update</c>. Tüm paketlerde açıktır (K13); abonelik yazma kapısı komutlara uygulanır.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/clinics/{clinicId:guid}/site")]
[Produces("application/json")]
[Authorize]
public sealed class ClinicSiteController : ControllerBase
{
    /// <summary>Yapılandırma sınırının üstünde taşıma payı; asıl sınır domain'dedir (<see cref="ClinicSiteConfig.MaxBytes"/>).</summary>
    private const long MaxDraftRequestBytes = 2 * ClinicSiteConfig.MaxBytes;

    /// <summary>Görsel sınırı + multipart ek yükü.</summary>
    private const long MaxImageRequestBytes = ClinicSiteImage.MaxSizeBytes + 1024 * 1024;

    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ClinicSiteController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>Taslak, durum, görseller ve yayın zorunlu listesi. Site yoksa <c>exists=false</c> ve önerilen adres.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Clinics.Read)]
    [ProducesResponseType(typeof(ClinicSiteDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] Guid clinicId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new GetClinicSiteQuery(clinicId), ct)).ToActionResult(this);
    }

    /// <summary>Taslağı kaydeder; ilk kayıt siteyi önerilen adresle oluşturur. Site varsa <c>rowVersion</c> zorunlu.</summary>
    [HttpPut("draft")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxDraftRequestBytes)]
    [ProducesResponseType(typeof(ClinicSiteStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SaveDraft([FromRoute] Guid clinicId, [FromBody] ClinicSiteDraftBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var config = body.Config.ValueKind == JsonValueKind.Undefined ? null : body.Config.GetRawText();
        return (await _mediator.Send(new SaveClinicSiteDraftCommand(clinicId, config, body.RowVersion), ct)).ToActionResult(this);
    }

    /// <summary>Adres uygunluğu; kendi güncel ya da eski adres uygundur.</summary>
    [HttpGet("address-availability")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [ProducesResponseType(typeof(ClinicSiteAddressAvailabilityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CheckAddress([FromRoute] Guid clinicId, [FromQuery] string? slug, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new CheckClinicSiteAddressQuery(clinicId, slug), ct)).ToActionResult(this);
    }

    /// <summary>Adresi değiştirir; yayınlanmış sitenin eski adresi yeni adrese yönlenir ve başkasına verilmez.</summary>
    [HttpPut("address")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClinicSiteStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeAddress([FromRoute] Guid clinicId, [FromBody] ClinicSiteAddressBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new ChangeClinicSiteAddressCommand(clinicId, body.Slug), ct)).ToActionResult(this);
    }

    /// <summary>Taslağı yeni sürüm olarak yayınlar; zorunlu liste eksikse 409 ve eksikler.</summary>
    [HttpPost("publish")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClinicSiteStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Publish([FromRoute] Guid clinicId, [FromBody] ClinicSiteRowVersionBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new PublishClinicSiteCommand(clinicId, body.RowVersion), ct)).ToActionResult(this);
    }

    /// <summary>Sürüm geçmişi, en yeni önce; yapılandırma dönmez.</summary>
    [HttpGet("versions")]
    [Authorize(Policy = PermissionCatalog.Clinics.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<ClinicSiteVersionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVersions([FromRoute] Guid clinicId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new GetClinicSiteVersionsQuery(clinicId), ct)).ToActionResult(this);
    }

    /// <summary>Sürümü taslağa kopyalar; yayın değişmez.</summary>
    [HttpPost("versions/{versionNumber:int}/restore")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClinicSiteStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RestoreVersion(
        [FromRoute] Guid clinicId,
        [FromRoute] int versionNumber,
        [FromBody] ClinicSiteRowVersionBody body,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new RestoreClinicSiteVersionCommand(clinicId, versionNumber, body.RowVersion), ct)).ToActionResult(this);
    }

    /// <summary>Siteyi geçici kapatır; idempotent.</summary>
    [HttpPost("close")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [ProducesResponseType(typeof(ClinicSiteStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Close([FromRoute] Guid clinicId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new CloseClinicSiteCommand(clinicId), ct)).ToActionResult(this);
    }

    /// <summary>Geçici kapatmayı kaldırır; idempotent.</summary>
    [HttpPost("reopen")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [ProducesResponseType(typeof(ClinicSiteStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reopen([FromRoute] Guid clinicId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new ReopenClinicSiteCommand(clinicId), ct)).ToActionResult(this);
    }

    /// <summary>Görsel yükler (<c>multipart/form-data</c>, alan <c>file</c>); tür içerikten belirlenir.</summary>
    [HttpPost("images")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxImageRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImageRequestBytes)]
    [ProducesResponseType(typeof(ClinicSiteImageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> UploadImage([FromRoute] Guid clinicId, [FromForm] StoredFileUploadForm form, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var valid = StoredFileHttpResults.ValidateUpload(form.File);
        if (!valid.IsSuccess)
            return valid.ToActionResult(this);

        await using var content = form.File!.OpenReadStream();
        var result = await _mediator.Send(new UploadClinicSiteImageCommand(clinicId, form.File.Length, content), ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult(this);
    }

    /// <summary>Panel önizlemesi için görsel içeriği (Bearer); yayınlanmamış görseli de açar, önbelleğe alınmaz.</summary>
    [HttpGet("images/{imageId:guid}/content")]
    [Authorize(Policy = PermissionCatalog.Clinics.Read)]
    [Produces("application/octet-stream", "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetImageContent([FromRoute] Guid clinicId, [FromRoute] Guid imageId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new GetClinicSiteImageContentQuery(clinicId, imageId), ct)).ToPrivateImage(this);
    }

    /// <summary>Görseli siler; taslakta ya da yayındaki sürümde kullanılıyorsa 409. Yanıttaki <c>rowVersion</c> sonraki yazmada kullanılır.</summary>
    [HttpDelete("images/{imageId:guid}")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [ProducesResponseType(typeof(ClinicSiteStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteImage([FromRoute] Guid clinicId, [FromRoute] Guid imageId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new DeleteClinicSiteImageCommand(clinicId, imageId), ct)).ToActionResult(this);
    }
}

/// <summary>PUT /api/v1/clinics/{clinicId}/site/draft gövdesi; <see cref="Config"/> yorumlanmadan saklanır.</summary>
public sealed class ClinicSiteDraftBody
{
    public JsonElement Config { get; init; }

    /// <summary>Site varsa zorunlu (Base64, 8 bayt).</summary>
    public string? RowVersion { get; init; }
}

public sealed class ClinicSiteAddressBody
{
    public string? Slug { get; init; }
}

public sealed class ClinicSiteRowVersionBody
{
    public string? RowVersion { get; init; }
}
