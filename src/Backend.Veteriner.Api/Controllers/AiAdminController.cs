using Asp.Versioning;
using Backend.Veteriner.Api.Auth;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Ai;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// AI denemesi yönetimi (ADR-044): klinik bazında açma/kapama ve bu ayın kullanımı. Yalnız platform yöneticisi
/// (<c>platform_admin</c> claim'i); klinik rolleri 403. Kiracılar arası. Sözleşme: docs/AI_HISTORY_SUMMARY_API_CONTRACT.md §3.
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

    /// <summary>Bu ayın (İstanbul takvim ayı) kullanımı: sistem toplamı, sınırlar, klinik bazında (AI'si açık olanlar dahil). Maliyet tahminidir.</summary>
    [HttpGet("usage")]
    [ProducesResponseType(typeof(AiAdminUsageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUsage(CancellationToken ct)
        => (await _mediator.Send(new GetAiAdminUsageQuery(), ct)).ToActionResult(this);

    /// <summary>Kliniğin AI'sini açar/kapatır (varsayılan kapalı). Denetim kaydına yazılır.</summary>
    [HttpPut("clinics/{clinicId:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AiAdminClinicDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetClinicEnabled([FromRoute] Guid clinicId, [FromBody] AiClinicSettingsBody body, CancellationToken ct)
        => (await _mediator.Send(new SetClinicAiEnabledCommand(clinicId, body.IsEnabled), ct)).ToActionResult(this);
}

public sealed class AiClinicSettingsBody
{
    public bool IsEnabled { get; init; }
}
