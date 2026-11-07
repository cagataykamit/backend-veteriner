using Asp.Versioning;
using Backend.Veteriner.Api.Auth;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.ClinicSites;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Klinik sitesi desteği (ADR-045 K10): yalnız platform yöneticisi (<c>platform_admin</c> claim'i); klinik rolleri 403.
/// Sözleşme: docs/CLINIC_SITE_API_CONTRACT.md §4.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/clinic-sites")]
[Authorize(Policy = AuthorizationPolicyNames.PlatformAdmin)]
[PlatformTenantScope]
[Produces("application/json")]
public sealed class ClinicSitesAdminController : ControllerBase
{
    private readonly IMediator _mediator;

    public ClinicSitesAdminController(IMediator mediator) => _mediator = mediator;

    /// <summary>Adres değiştirme hakkı biten siteye ek hak tanır; site güncel ya da eski adresiyle bulunur.</summary>
    [HttpPost("{slug}/address-change-grants")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClinicSiteStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GrantAddressChanges([FromRoute] string slug, [FromBody] ClinicSiteAddressChangeGrantBody body, CancellationToken ct)
        => (await _mediator.Send(new GrantClinicSiteAddressChangesCommand(slug, body.AdditionalChanges), ct)).ToActionResult(this);
}

public sealed class ClinicSiteAddressChangeGrantBody
{
    /// <summary>Eklenecek hak (1-3).</summary>
    public int AdditionalChanges { get; init; }
}
