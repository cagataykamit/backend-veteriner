using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.SpeciesReference.Commands.Create;
using Backend.Veteriner.Application.SpeciesReference.Commands.Update;
using Backend.Veteriner.Application.SpeciesReference.Contracts.Dtos;
using Backend.Veteriner.Application.SpeciesReference.Queries.GetById;
using Backend.Veteriner.Application.SpeciesReference.Queries.GetList;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Global tür (species) referans verisi. Tenant bağlamı gerektirmez.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/species")]
[Produces("application/json")]
[Authorize]
public sealed class SpeciesController : ControllerBase
{
    private readonly IMediator _mediator;

    public SpeciesController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Species.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateSpeciesCommand cmd, CancellationToken ct)
    {
        var result = await _mediator.Send(cmd, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var id = result.Value;
        return CreatedAtAction(
            nameof(GetById),
            new { version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0", id },
            id);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Species.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSpeciesCommand cmd, CancellationToken ct)
    {
        // Route id kaynak-of-truth: body'de id yoksa route'tan enjekte edilir.
        // Body'de id gönderildiyse ve route ile uyuşmuyorsa açıklayıcı hata dön.
        if (cmd.Id != Guid.Empty && id != cmd.Id)
            return Result.Failure("Species.RouteIdMismatch", "Route id ile body id uyuşmuyor.").ToActionResult(this);

        cmd = cmd with { Id = id };

        var result = await _mediator.Send(cmd, ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Species.Read)]
    [ProducesResponseType(typeof(SpeciesDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSpeciesByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Species.Read)]
    [ProducesResponseType(typeof(PagedResult<SpeciesListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList([FromQuery] PageRequest page, [FromQuery] bool? isActive, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSpeciesListQuery(page, isActive), ct);
        return result.ToActionResult(this);
    }
}
