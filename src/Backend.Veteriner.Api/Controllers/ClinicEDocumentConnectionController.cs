using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.EDocuments.Connections;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Kliniğin kendi e-belge entegratör hesabı bağlantısı (ADR-036 AK-1 a, E_DOCUMENTS_API_CONTRACT §3). Kimlik bilgisi şifreli
/// saklanır ve hiçbir yanıtta dönmez. Okuma <c>Clinics.Read</c>, yazma <c>Clinics.Update</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/clinics/{clinicId:guid}/e-document-connection")]
[Produces("application/json")]
[Authorize]
public sealed class ClinicEDocumentConnectionController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ClinicEDocumentConnectionController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Clinics.Read)]
    [ProducesResponseType(typeof(EDocumentConnectionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] Guid clinicId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetEDocumentConnectionQuery(clinicId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Yetkilendirme adresini üretir (OAuth 2.0 + PKCE); panel tarayıcıyı bu adrese yönlendirir.</summary>
    [HttpPost("authorize")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(EDocumentAuthorizationStartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Authorize([FromRoute] Guid clinicId, [FromBody] EDocumentAuthorizeBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new StartEDocumentAuthorizationCommand(clinicId, body.Provider), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Kullanıcının birden çok firması varsa bağlanacak hesabı seçer.</summary>
    [HttpPut("account")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(EDocumentConnectionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SelectAccount([FromRoute] Guid clinicId, [FromBody] EDocumentAccountBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new SelectEDocumentAccountCommand(clinicId, body.AccountId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Bağlantıyı sınar (belirteç, firma erişimi, e-belge kullanımı).</summary>
    [HttpPost("verify")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [ProducesResponseType(typeof(EDocumentConnectionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Verify([FromRoute] Guid clinicId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new VerifyEDocumentConnectionCommand(clinicId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Bağlantıyı koparır; şifreli kimlik bilgisi silinir, kesilmiş belgeler kalır.</summary>
    [HttpDelete]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Disconnect([FromRoute] Guid clinicId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new DisconnectEDocumentConnectionCommand(clinicId), ct);
        return result.ToActionResult(this);
    }
}

public sealed class EDocumentAuthorizeBody
{
    public string? Provider { get; init; }
}

public sealed class EDocumentAccountBody
{
    public string? AccountId { get; init; }
}
