using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.OwnerPortal;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalAppointments;
using Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalBalance;
using Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalClinic;
using Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalPayments;
using Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalPet;
using Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalPets;
using Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalPetVaccinations;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Veteriner.Api.Controllers.Portal;

/// <summary>
/// Hasta sahibi portalı dilim B salt okunur uçları (ADR-031 v0; docs/PORTAL_API_CONTRACT.md §6): klinik, hayvanlar, aşı,
/// randevu, bakiye ve ödeme geçmişi. Her uç bağ sahipliğini <c>OwnerPortalAccess</c> ile denetler ve okuma denetim kaydı yazar.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/portal/v{version:apiVersion}/links/{linkId:guid}")]
[Produces("application/json")]
[Authorize(Policy = OwnerPortalAuth.Policy)]
[EnableRateLimiting("portal")]
public sealed class PortalLinkRecordsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PortalLinkRecordsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Bağın kliniği: iletişim bilgisi ve çalışma saatleri.</summary>
    [HttpGet("clinic")]
    [ProducesResponseType(typeof(PortalClinicDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetClinic([FromRoute] Guid linkId, CancellationToken ct)
        => SendAsync(ownerAccountId => new GetPortalClinicQuery(ownerAccountId, linkId), ct);

    /// <summary>Bağın müşterisinin hayvanları.</summary>
    [HttpGet("pets")]
    [ProducesResponseType(typeof(IReadOnlyList<PortalPetDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetPets([FromRoute] Guid linkId, CancellationToken ct)
        => SendAsync(ownerAccountId => new GetPortalPetsQuery(ownerAccountId, linkId), ct);

    /// <summary>Tek hayvanın kimlik bilgisi.</summary>
    [HttpGet("pets/{petId:guid}")]
    [ProducesResponseType(typeof(PortalPetDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetPet([FromRoute] Guid linkId, [FromRoute] Guid petId, CancellationToken ct)
        => SendAsync(ownerAccountId => new GetPortalPetQuery(ownerAccountId, linkId, petId), ct);

    /// <summary>Hayvanın aşı geçmişi ve planlı aşıları (ADR-028 gecikme tanımı).</summary>
    [HttpGet("pets/{petId:guid}/vaccinations")]
    [ProducesResponseType(typeof(PortalPetVaccinationsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetPetVaccinations([FromRoute] Guid linkId, [FromRoute] Guid petId, CancellationToken ct)
        => SendAsync(ownerAccountId => new GetPortalPetVaccinationsQuery(ownerAccountId, linkId, petId), ct);

    /// <summary>Randevular: <c>scope=upcoming</c> (varsayılan) veya <c>past</c>.</summary>
    [HttpGet("appointments")]
    [ProducesResponseType(typeof(PagedResult<PortalAppointmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetAppointments(
        [FromRoute] Guid linkId,
        [FromQuery] string? scope,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
        => SendAsync(ownerAccountId => new GetPortalAppointmentsQuery(
            ownerAccountId,
            linkId,
            scope ?? PortalAppointmentScope.Upcoming,
            page ?? 1,
            pageSize ?? OwnerPortalPaging.DefaultPageSize), ct);

    /// <summary>Bağın kliniğindeki bakiye (yalnız tutar/durum).</summary>
    [HttpGet("balance")]
    [ProducesResponseType(typeof(PortalBalanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetBalance([FromRoute] Guid linkId, CancellationToken ct)
        => SendAsync(ownerAccountId => new GetPortalBalanceQuery(ownerAccountId, linkId), ct);

    /// <summary>Bağın kliniğindeki ödeme geçmişi (tarih, tutar, yöntem).</summary>
    [HttpGet("payments")]
    [ProducesResponseType(typeof(PagedResult<PortalPaymentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetPayments(
        [FromRoute] Guid linkId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
        => SendAsync(ownerAccountId => new GetPortalPaymentsQuery(
            ownerAccountId, linkId, page ?? 1, pageSize ?? OwnerPortalPaging.DefaultPageSize), ct);

    private async Task<IActionResult> SendAsync<TResponse>(
        Func<Guid, IRequest<Result<TResponse>>> createQuery,
        CancellationToken ct)
    {
        if (!this.TryGetOwnerAccountId(out var ownerAccountId, out var problem))
            return problem!;

        var result = await _mediator.Send(createQuery(ownerAccountId), ct);
        return result.ToActionResult(this);
    }
}
