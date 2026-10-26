using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Application.Files.Queries.GetSignedContent;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// İmzalı URL ile dosya içeriği (IMAGING_API_CONTRACT.md §5). Bearer gerekmez; yetki, URL'yi üreten uçta (sahip kaydın okuma izni
/// + klinik kapsamı) denetlenmiştir. İmza bozuk/eksik/süresi geçmiş: 403 <c>Files.AccessUrlInvalid</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/files")]
[AllowAnonymous]
public sealed class FilesController(IMediator mediator) : ControllerBase
{
    [HttpGet("signed/{fileId:guid}")]
    [Produces("application/octet-stream", "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSignedContent(
        [FromRoute] Guid fileId,
        [FromQuery] long expires,
        [FromQuery] string? signature,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetSignedStoredFileContentQuery(fileId, expires, signature), ct);
        return result.ToFileResult(this, download: false);
    }
}
