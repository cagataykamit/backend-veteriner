using Asp.Versioning;
using Backend.Veteriner.Api.Auth;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Ai;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// AI yönetimi (ADR-044; AI-007 kota, AI-008 maliyet): klinik bazında açma/kapama, fon ekleme, kredi durumu ve aylık kullanım
/// kırılımları. Yalnız platform yöneticisi (<c>platform_admin</c> claim'i); klinik rolleri 403. Kiracılar arası. Klinik metin
/// dönmez. Sözleşme: docs/AI_HISTORY_SUMMARY_API_CONTRACT.md §3.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/ai")]
[Authorize(Policy = AuthorizationPolicyNames.PlatformAdmin)]
[PlatformTenantScope]
[Produces("application/json")]
public sealed class AiAdminController : ControllerBase
{
    private readonly IMediator _mediator;

    public AiAdminController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Ayın (İstanbul takvim ayı, <c>month=yyyy-MM</c>; boşsa bu ay) kullanımı: sistem toplamı, sistem sınırına uzaklık, klinik /
    /// özellik / model kırılımı. Gerçek token, kredi ve tahmini maliyet (fatura değildir).
    /// </summary>
    [HttpGet("usage")]
    [ProducesResponseType(typeof(AiAdminUsageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUsage([FromQuery] string? month, CancellationToken ct)
        => (await _mediator.Send(new GetAiAdminUsageQuery(month), ct)).ToActionResult(this);

    /// <summary>
    /// Klinikler ve bu ayın kredi durumu. <c>search</c> yoksa AI ayarı olan klinikler (açıklar önce); varsa klinik ya da kiracı
    /// adına uyan en çok 50 klinik (en az 2 karakter).
    /// </summary>
    [HttpGet("clinics")]
    [ProducesResponseType(typeof(IReadOnlyList<AiAdminClinicDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetClinics([FromQuery] string? search, CancellationToken ct)
        => (await _mediator.Send(new GetAiAdminClinicsQuery(search), ct)).ToActionResult(this);

    /// <summary>Kliniğin AI'sini açar/kapatır (varsayılan kapalı). Denetim kaydına yazılır.</summary>
    [HttpPut("clinics/{clinicId:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AiAdminClinicDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetClinicEnabled([FromRoute] Guid clinicId, [FromBody] AiClinicSettingsBody body, CancellationToken ct)
        => (await _mediator.Send(new SetClinicAiEnabledCommand(clinicId, body.IsEnabled), ct)).ToActionResult(this);

    /// <summary>Kliniğin fon hareketleri (yeniden eskiye, en çok 100).</summary>
    [HttpGet("clinics/{clinicId:guid}/fund-grants")]
    [ProducesResponseType(typeof(IReadOnlyList<AiFundGrantDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetFundGrants([FromRoute] Guid clinicId, CancellationToken ct)
        => (await _mediator.Send(new GetAiFundGrantsQuery(clinicId), ct)).ToActionResult(this);

    /// <summary>Kliniğe elle fon ekler (1–100.000 kredi; aylık kotadan sonra harcanır, devreder). Denetim kaydına yazılır.</summary>
    [HttpPost("clinics/{clinicId:guid}/fund-grants")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AiAdminClinicDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GrantFund([FromRoute] Guid clinicId, [FromBody] AiFundGrantBody body, CancellationToken ct)
        => (await _mediator.Send(new GrantAiFundCommand(clinicId, body.Credits, body.Note), ct)).ToActionResult(this);
}

public sealed class AiClinicSettingsBody
{
    public bool IsEnabled { get; init; }
}

public sealed class AiFundGrantBody
{
    public int Credits { get; init; }
    public string? Note { get; init; }
}
