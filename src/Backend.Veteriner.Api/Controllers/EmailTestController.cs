using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public sealed class EmailTestController : ControllerBase
{
    // Art�k transactional de�il, direkt SMTP
    private readonly IEmailSenderImmediate _immediate;

    public EmailTestController(IEmailSenderImmediate immediate)
        => _immediate = immediate;

    public sealed record SendEmailRequest(
        string To,
        string Subject,
        string? HtmlBody,
        string? Body
    );

    /// <summary>
    /// Basit e-posta g�nderim testi.
    /// HtmlBody doluysa HTML, yoksa Body �zerinden d�z metin g�nderir.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("send")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Send([FromBody] SendEmailRequest? req, CancellationToken ct)
    {
        if (req is null)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request",
                detail: "Ge�ersiz istek g�vdesi."
            );
        }

        if (string.IsNullOrWhiteSpace(req.To))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation failed",
                detail: "To zorunludur."
            );
        }

        var isHtml = !string.IsNullOrWhiteSpace(req.HtmlBody);
        var content = isHtml
            ? req.HtmlBody!
            : (req.Body ?? string.Empty);

        try
        {
            await _immediate.SendAsync(req.To, req.Subject ?? string.Empty, content, ct, isHtml);
            return Ok(new { message = "E-posta g�nderildi." });
        }
        catch (Exception ex)
        {
            // Te�his ama�l�; prod ortamda ham mesaj� d��ar� vermeyin.
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Email send failed",
                detail: ex.Message
            );
        }
    }
}
