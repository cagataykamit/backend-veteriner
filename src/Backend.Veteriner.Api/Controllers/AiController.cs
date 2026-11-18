using System.Text.Json.Nodes;
using Asp.Versioning;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Ai;
using Backend.Veteriner.Application.Ai.ClinicalDrafts;
using Backend.Veteriner.Application.Ai.Drafts;
using Backend.Veteriner.Application.Ai.PetHistorySummary;
using Backend.Veteriner.Application.Auth;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Gömülü AI yardımcısı (ADR-008, ADR-044): yalnız taslak üretir, sağlayıcı anahtarı yalnız backend'dedir; AI kullanılamazsa
/// klinik iş akışı etkilenmez. İzin <c>Ai.Use</c>; ayrıca kliniğin AI'si açık olmalıdır. Sözleşme: docs/AI_HISTORY_SUMMARY_API_CONTRACT.md.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
[Authorize(Policy = PermissionCatalog.Ai.Use)]
[Produces("application/json")]
public sealed class AiController : ControllerBase
{
    private readonly IMediator _mediator;

    public AiController(IMediator mediator) => _mediator = mediator;

    /// <summary>Aktif klinikte AI kullanılabilir mi; arayüz düğmeyi buna göre gösterir. Dolar tutarı içermez.</summary>
    [HttpGet("ai/status")]
    [ProducesResponseType(typeof(AiStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
        => (await _mediator.Send(new GetAiStatusQuery(), ct)).ToActionResult(this);

    /// <summary>
    /// Hasta geçmişi özeti taslağı (AI-002): son muayene, tedavi, reçete, lab, aşı ve yatış kayıtlarından (kullanıcının okuma izni
    /// olan türler, aktif klinik) bölümlü özet; her madde kaynak kayıtlara bağlıdır. Kaydedilmez. Müşteri kimlik bilgisi modele gitmez.
    /// </summary>
    [HttpPost("pets/{id:guid}/ai/history-summary")]
    [Authorize(Policy = PermissionCatalog.Pets.Read)]
    [ProducesResponseType(typeof(PetHistoryAiSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GeneratePetHistorySummary([FromRoute] Guid id, CancellationToken ct)
        => (await _mediator.Send(new GeneratePetHistorySummaryCommand(id), ct)).ToActionResult(this);

    /// <summary>
    /// Muayene notu taslağı (AI-001): hekimin serbest notunu muayene alanlarına ayırır. Muayeneye yazmaz; kabul
    /// <c>ai/drafts/{id}/accept</c> ile. Serbest not saklanmaz. Kesinleşmiş muayenede 409. Sözleşme: docs/AI_DRAFTS_API_CONTRACT.md §7.1.
    /// </summary>
    [HttpPost("examinations/{id:guid}/ai/note-draft")]
    [Authorize(Policy = PermissionCatalog.Examinations.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AiDraftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GenerateExamNoteDraft([FromRoute] Guid id, [FromBody] AiNoteDraftBody body, CancellationToken ct)
        => (await _mediator.Send(new GenerateExamNoteDraftCommand(id, body.Text ?? string.Empty), ct)).ToActionResult(this);

    /// <summary>
    /// Hasta sahibine sade açıklama taslağı (AI-003): muayene kaydından (ve bağlı reçete/tedaviden). Kabulde muayeneye bağlı ayrı
    /// açıklama kaydı oluşur. Sözleşme: docs/AI_DRAFTS_API_CONTRACT.md §7.2.
    /// </summary>
    [HttpPost("examinations/{id:guid}/ai/owner-explanation-draft")]
    [Authorize(Policy = PermissionCatalog.Examinations.Read)]
    [Authorize(Policy = PermissionCatalog.Examinations.Update)]
    [ProducesResponseType(typeof(AiDraftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GenerateOwnerExplanationDraft([FromRoute] Guid id, [FromBody] AiNoteDraftBody? body, CancellationToken ct)
        => (await _mediator.Send(new GenerateOwnerExplanationDraftCommand(id, body?.Text), ct)).ToActionResult(this);

    /// <summary>
    /// Taburcu / evde bakım talimatı taslağı (AI-004), muayeneden. Kabulde muayeneye bağlı bakım talimatı oluşur.
    /// Sözleşme: docs/AI_DRAFTS_API_CONTRACT.md §7.3.
    /// </summary>
    [HttpPost("examinations/{id:guid}/ai/care-instruction-draft")]
    [Authorize(Policy = PermissionCatalog.Examinations.Read)]
    [Authorize(Policy = PermissionCatalog.CareInstructions.Create)]
    [ProducesResponseType(typeof(AiDraftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GenerateExaminationCareInstructionDraft(
        [FromRoute] Guid id, [FromBody] AiNoteDraftBody? body, CancellationToken ct)
        => (await _mediator.Send(
            new GenerateDischargeInstructionsDraftCommand(DischargeDraftSource.Examination, id, body?.Text), ct)).ToActionResult(this);

    /// <summary>
    /// Taburcu / evde bakım talimatı taslağı (AI-004), yatıştan (ve yatışın muayenesinden). Kabulde yatışa bağlı bakım talimatı
    /// oluşur. İptal edilmiş yatışta 409. Sözleşme: docs/AI_DRAFTS_API_CONTRACT.md §7.3.
    /// </summary>
    [HttpPost("hospitalizations/{id:guid}/ai/care-instruction-draft")]
    [Authorize(Policy = PermissionCatalog.Hospitalizations.Read)]
    [Authorize(Policy = PermissionCatalog.CareInstructions.Create)]
    [ProducesResponseType(typeof(AiDraftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GenerateHospitalizationCareInstructionDraft(
        [FromRoute] Guid id, [FromBody] AiNoteDraftBody? body, CancellationToken ct)
        => (await _mediator.Send(
            new GenerateDischargeInstructionsDraftCommand(DischargeDraftSource.Hospitalization, id, body?.Text), ct)).ToActionResult(this);

    /// <summary>
    /// AI taslağı (AI-009): içerik yalnız karar bekleyen taslakta gelir. Aktif klinik taslağın kliniği olmalı; özelliğin izni
    /// (ör. muayene notu için <c>Examinations.Update</c>) gerekir. Sözleşme: docs/AI_DRAFTS_API_CONTRACT.md.
    /// </summary>
    [HttpGet("ai/drafts/{id:guid}")]
    [ProducesResponseType(typeof(AiDraftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDraft([FromRoute] Guid id, CancellationToken ct)
        => (await _mediator.Send(new GetAiDraftQuery(id), ct)).ToActionResult(this);

    /// <summary>Bir kaydın karar bekleyen AI taslakları (yeniden eskiye, en çok 20); yalnız kullanıcının izinli olduğu özellikler.</summary>
    [HttpGet("ai/drafts")]
    [ProducesResponseType(typeof(IReadOnlyList<AiDraftDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListPendingDrafts([FromQuery] string targetType, [FromQuery] Guid targetId, CancellationToken ct)
        => (await _mediator.Send(new ListPendingAiDraftsQuery(targetType, targetId), ct)).ToActionResult(this);

    /// <summary>
    /// Taslağı kabul eder; <c>editedContent</c> verilirse düzenlenmiş hali yazılır. İçerik özelliğin mevcut komutlarıyla hedef
    /// kayda tek transaction'da yazılır; başarısızsa hiçbir şey yazılmaz ve taslak bekler.
    /// </summary>
    [HttpPost("ai/drafts/{id:guid}/accept")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AiDraftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AcceptDraft([FromRoute] Guid id, [FromBody] AiDraftAcceptBody? body, CancellationToken ct)
        => (await _mediator.Send(new AcceptAiDraftCommand(id, body?.EditedContent), ct)).ToActionResult(this);

    /// <summary>Taslağı atar; hiçbir kayda yazılmaz.</summary>
    [HttpPost("ai/drafts/{id:guid}/discard")]
    [ProducesResponseType(typeof(AiDraftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    public async Task<IActionResult> DiscardDraft([FromRoute] Guid id, CancellationToken ct)
        => (await _mediator.Send(new DiscardAiDraftCommand(id), ct)).ToActionResult(this);
}

public sealed class AiDraftAcceptBody
{
    /// <summary>Kullanıcının düzenlediği içerik (taslakla aynı şemada); <c>null</c> ise üretilen içerik olduğu gibi kabul edilir.</summary>
    public JsonObject? EditedContent { get; init; }
}

public sealed class AiNoteDraftBody
{
    /// <summary>
    /// Kullanıcının yazdığı serbest not: AI-001'de bölünecek muayene notu (zorunlu, en çok 8000 karakter); AI-003/004'te hekimin
    /// vurgulanmasını istediği noktalar (isteğe bağlı, en çok 2000 karakter). Saklanmaz.
    /// </summary>
    public string? Text { get; init; }
}
