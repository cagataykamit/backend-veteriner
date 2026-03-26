using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.BreedsReference.Commands.Create;
using Backend.Veteriner.Application.BreedsReference.Commands.Update;
using Backend.Veteriner.Application.BreedsReference.Contracts.Dtos;
using Backend.Veteriner.Application.BreedsReference.Queries.GetById;
using Backend.Veteriner.Application.BreedsReference.Queries.GetList;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Global ırk (breed) referans verisi. Tenant bağlamı gerektirmez.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/breeds")]
[Produces("application/json")]
[Authorize]
public sealed class BreedsController : ControllerBase
{
    private readonly IMediator _mediator;

    public BreedsController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Breeds.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateBreedCommand cmd, CancellationToken ct)
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
    [Authorize(Policy = PermissionCatalog.Breeds.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBreedCommand cmd, CancellationToken ct)
    {
        // Frontend çoğu zaman body'de id göndermeyebilir; route id kaynak-of-truth.
        // Body'de id gönderildiyse ve route ile uyuşmuyorsa açıklayıcı hata dön.
        if (cmd.Id != Guid.Empty && id != cmd.Id)
            return Result.Failure("Breeds.RouteIdMismatch", "Route id ile body id uyuşmuyor.").ToActionResult(this);

        cmd = cmd with { Id = id };

        var result = await _mediator.Send(cmd, ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Breeds.Read)]
    [ProducesResponseType(typeof(BreedDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetBreedByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Breeds.Read)]
    [ProducesResponseType(typeof(PagedResult<BreedListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(
        [FromQuery] PageRequest page,
        [FromQuery] bool? isActive,
        [FromQuery] Guid? speciesId,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new GetBreedListQuery(page, isActive, speciesId), ct);
        return result.ToActionResult(this);
    }
}
