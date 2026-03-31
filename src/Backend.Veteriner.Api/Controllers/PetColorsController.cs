using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.PetColors.Contracts.Dtos;
using Backend.Veteriner.Application.PetColors.Queries.GetList;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Global renk (pet color) referans verisi. Tenant bağlamı gerektirmez.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/pet-colors")]
[Produces("application/json")]
[Authorize]
public sealed class PetColorsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PetColorsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Aktif renkleri displayOrder, sonra name sırasıyla listeler.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Pets.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<PetColorListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPetColorsListQuery(), ct);
        return result.ToActionResult(this);
    }
}
