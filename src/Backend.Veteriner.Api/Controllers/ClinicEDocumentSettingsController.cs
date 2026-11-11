using System.Text.Json.Serialization;
using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.EDocuments.Automation;
using Backend.Veteriner.Domain.EDocuments;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Klinik e-Belge ayarı (E_DOCUMENTS_API_CONTRACT §3.7): otomatik e-belge modu, gün sonu saati, ödeme ekranı varsayılanı ve
/// mükellef türünden çıkan belge türü özeti. Okuma <c>Clinics.Read</c>, yazma <c>Clinics.Update</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/clinics/{clinicId:guid}/e-document-settings")]
[Produces("application/json")]
[Authorize]
public sealed class ClinicEDocumentSettingsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ClinicEDocumentSettingsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Clinics.Read)]
    [ProducesResponseType(typeof(ClinicEDocumentSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] Guid clinicId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetClinicEDocumentSettingsQuery(clinicId), ct);
        return result.ToActionResult(this);
    }

    [HttpPut]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClinicEDocumentSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Put([FromRoute] Guid clinicId, [FromBody] ClinicEDocumentSettingsBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new UpdateClinicEDocumentSettingsCommand(clinicId, body.AutoIssueMode, body.EndOfDayTime, body.IssueOnPaymentByDefault), ct);
        return result.ToActionResult(this);
    }
}

public sealed class ClinicEDocumentSettingsBody
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EDocumentAutoIssueMode? AutoIssueMode { get; init; }
    public string? EndOfDayTime { get; init; }
    public bool IssueOnPaymentByDefault { get; init; }
}
