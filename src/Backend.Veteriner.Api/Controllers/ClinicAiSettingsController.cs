using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Ai;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Klinik ayarlarında yapay zekâ (ADR-044 E1, TIER-013 Dilim 1): durum, kredi ve kredi geçmişi (<c>Clinics.Read</c>); klinik
/// yöneticisinin aç/kapa ve bilgilendirme kabulü (<c>Clinics.Update</c>). Dolar dönmez. Sözleşme:
/// docs/AI_HISTORY_SUMMARY_API_CONTRACT.md §3a.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/clinics/{clinicId:guid}/ai-settings")]
[Authorize]
[Produces("application/json")]
public sealed class ClinicAiSettingsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ClinicAiSettingsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>Kliniğin AI durumu, bu ayın kredisi, eklenen krediler ve açmak için gereken bilgilendirme.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Clinics.Read)]
    [ProducesResponseType(typeof(ClinicAiSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] Guid clinicId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new GetClinicAiSettingsQuery(clinicId), ct)).ToActionResult(this);
    }

    /// <summary>
    /// AI'yi açar/kapatır. Açarken güncel bilgilendirme kabul edilmemişse <c>acknowledgementVersion</c> güncel sürüm olmalıdır
    /// (yoksa 400 <c>Ai.AcknowledgementRequired</c>). Kapatmak veri silmez.
    /// </summary>
    [HttpPut]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClinicAiSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update([FromRoute] Guid clinicId, [FromBody] ClinicAiSettingsBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new UpdateClinicAiSettingsCommand(clinicId, body.IsEnabled, body.AcknowledgementVersion), ct))
            .ToActionResult(this);
    }
}

public sealed class ClinicAiSettingsBody
{
    public bool IsEnabled { get; init; }

    /// <summary>Kabul edilen bilgilendirme sürümü; yalnız ilk açılışta (ya da sürüm değişince) gerekir.</summary>
    public int? AcknowledgementVersion { get; init; }
}
