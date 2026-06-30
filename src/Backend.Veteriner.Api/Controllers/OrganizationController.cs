using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Organization.Commands.UpdateBillingProfile;
using Backend.Veteriner.Application.Organization.Contracts.Dtos;
using Backend.Veteriner.Application.Organization.Contracts.Requests;
using Backend.Veteriner.Application.Organization.Queries.GetBillingProfile;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/organization")]
[Produces("application/json")]
[Authorize]
public sealed class OrganizationController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public OrganizationController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpGet("billing-profile")]
    [Authorize(Policy = PermissionCatalog.Tenants.Read)]
    [ProducesResponseType(typeof(OrganizationBillingProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetBillingProfile(CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetOrganizationBillingProfileQuery(), ct);
        return result.ToActionResult(this);
    }

    [HttpPut("billing-profile")]
    [Authorize(Policy = PermissionCatalog.Tenants.InviteCreate)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(OrganizationBillingProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> PutBillingProfile(
        [FromBody] UpdateOrganizationBillingProfileRequest request,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new UpdateOrganizationBillingProfileCommand(
                request.CompanyName,
                request.LegalCompanyName,
                request.TaxOffice,
                request.TaxNumber,
                request.CompanyPhone,
                request.InvoiceProvince,
                request.InvoiceDistrict,
                request.InvoiceNeighborhood,
                request.InvoiceStreet,
                request.InvoiceBuildingName,
                request.InvoiceBuildingNo,
                request.InvoiceDoorNo),
            ct);
        return result.ToActionResult(this);
    }
}
