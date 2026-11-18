using Asp.Versioning;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Ai;
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
}
