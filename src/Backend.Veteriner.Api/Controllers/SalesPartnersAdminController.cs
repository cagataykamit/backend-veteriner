using Asp.Versioning;
using Backend.Veteriner.Api.Auth;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Api.Contracts;
using Backend.Veteriner.Application.SalesPartners.Commands.CancelCollection;
using Backend.Veteriner.Application.SalesPartners.Commands.CreateCode;
using Backend.Veteriner.Application.SalesPartners.Commands.CreatePartner;
using Backend.Veteriner.Application.SalesPartners.Commands.MarkCommissionsPaid;
using Backend.Veteriner.Application.SalesPartners.Commands.SetCodeActive;
using Backend.Veteriner.Application.SalesPartners.Commands.UpdateCode;
using Backend.Veteriner.Application.SalesPartners.Commands.UpdatePartner;
using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Application.SalesPartners.Queries.ExportCollections;
using Backend.Veteriner.Application.SalesPartners.Queries.GetPartnerById;
using Backend.Veteriner.Application.SalesPartners.Queries.GetPartners;
using Backend.Veteriner.Application.SalesPartners.Queries.GetRedemptions;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Satış ortağı ve indirim kodu yönetimi (ADR-037): yalnız platform yöneticisi (<c>platform_admin</c> claim'i); klinik rolleri 403.
/// Sözleşme: <c>docs/SALES_PARTNER_DISCOUNT_API_CONTRACT.md</c> §5.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/sales-partners")]
[Authorize(Policy = AuthorizationPolicyNames.PlatformAdmin)]
[Produces("application/json")]
public sealed class SalesPartnersAdminController : ControllerBase
{
    private readonly IMediator _mediator;

    public SalesPartnersAdminController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SalesPartnerDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(CancellationToken ct)
        => Ok(await _mediator.Send(new GetSalesPartnersQuery(), ct));

    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SalesPartnerDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] SalesPartnerBody body, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateSalesPartnerCommand(body.Name, body.Email, body.Phone, body.LinkedTenantId), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        return CreatedAtAction(nameof(GetById), new { version = ApiVersionText(), id = result.Value!.Id }, result.Value);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SalesPartnerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
        => (await _mediator.Send(new GetSalesPartnerByIdQuery(id), ct)).ToActionResult(this);

    [HttpPut("{id:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SalesPartnerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] SalesPartnerBody body, CancellationToken ct)
        => (await _mediator.Send(
            new UpdateSalesPartnerCommand(id, body.Name, body.Email, body.Phone, body.LinkedTenantId, body.IsActive), ct))
            .ToActionResult(this);

    [HttpPost("{id:guid}/codes")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(DiscountCodeDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCode([FromRoute] Guid id, [FromBody] DiscountCodeTermsBody body, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateDiscountCodeCommand(
            id,
            body.Code ?? string.Empty,
            body.DiscountPercent,
            body.CommissionPercent,
            body.ValidFromUtc,
            body.ValidUntilUtc,
            body.MaxRedemptions), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        return CreatedAtAction(nameof(GetById), new { version = ApiVersionText(), id }, result.Value);
    }

    [HttpPut("{id:guid}/codes/{codeId:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(DiscountCodeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCode(
        [FromRoute] Guid id,
        [FromRoute] Guid codeId,
        [FromBody] DiscountCodeTermsBody body,
        CancellationToken ct)
        => (await _mediator.Send(new UpdateDiscountCodeCommand(
            id,
            codeId,
            body.DiscountPercent,
            body.CommissionPercent,
            body.ValidFromUtc,
            body.ValidUntilUtc,
            body.MaxRedemptions), ct)).ToActionResult(this);

    [HttpPost("{id:guid}/codes/{codeId:guid}/deactivate")]
    [ProducesResponseType(typeof(DiscountCodeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateCode([FromRoute] Guid id, [FromRoute] Guid codeId, CancellationToken ct)
        => (await _mediator.Send(new SetDiscountCodeActiveCommand(id, codeId, false), ct)).ToActionResult(this);

    [HttpPost("{id:guid}/codes/{codeId:guid}/activate")]
    [ProducesResponseType(typeof(DiscountCodeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ActivateCode([FromRoute] Guid id, [FromRoute] Guid codeId, CancellationToken ct)
        => (await _mediator.Send(new SetDiscountCodeActiveCommand(id, codeId, true), ct)).ToActionResult(this);

    [HttpGet("{id:guid}/redemptions")]
    [ProducesResponseType(typeof(IReadOnlyList<DiscountRedemptionRowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRedemptions([FromRoute] Guid id, CancellationToken ct)
        => (await _mediator.Send(new GetSalesPartnerRedemptionsQuery(id), ct)).ToActionResult(this);

    [HttpPost("{id:guid}/commissions/mark-paid")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(MarkCommissionsPaidResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarkCommissionsPaid([FromRoute] Guid id, [FromBody] MarkCommissionsPaidBody body, CancellationToken ct)
        => (await _mediator.Send(new MarkCommissionsPaidCommand(id, body.CollectionIds, body.PaidAtUtc, body.Note), ct))
            .ToActionResult(this);

    [HttpPost("{id:guid}/collections/{collectionId:guid}/cancel")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(DiscountCollectionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelCollection(
        [FromRoute] Guid id,
        [FromRoute] Guid collectionId,
        [FromBody] CancelDiscountCollectionBody body,
        CancellationToken ct)
        => (await _mediator.Send(new CancelDiscountCollectionCommand(id, collectionId, body.Reason), ct)).ToActionResult(this);

    [HttpGet("export.csv")]
    [Produces(ReportFileContentTypes.CsvUtf8, "application/json")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportCsv([FromQuery] Guid? partnerId, CancellationToken ct)
    {
        var result = await _mediator.Send(new ExportSalesPartnerCollectionsQuery(partnerId), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        return File(result.Value!.ContentUtf8Bom, ReportFileContentTypes.CsvUtf8, result.Value.FileDownloadName);
    }

    private string ApiVersionText() => HttpContext.RequestedApiVersion?.ToString() ?? "1.0";
}
