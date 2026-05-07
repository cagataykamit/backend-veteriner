using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Tanılama amaçlı SMTP gönderim ucu. Üretimde kapalıdır:
/// 1) <see cref="ApiExplorerSettingsAttribute.IgnoreApi"/> ile Swagger yüzeyinden gizlenir.
/// 2) <see cref="PermissionCatalog.Admin.Diagnostics"/> policy zorunludur (anonim erişim yok).
/// 3) Non-Development ortamlarda <c>Send</c> 404 döner (savunma derinliği: yetki olsa bile gizli).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize(Policy = PermissionCatalog.Admin.Diagnostics)]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class EmailTestController : ControllerBase
{
    private readonly IEmailSenderImmediate _immediate;
    private readonly IHostEnvironment _environment;

    public EmailTestController(IEmailSenderImmediate immediate, IHostEnvironment environment)
    {
        _immediate = immediate;
        _environment = environment;
    }

    public sealed record SendEmailRequest(
        string To,
        string Subject,
        string? HtmlBody,
        string? Body
    );

    /// <summary>
    /// Basit e-posta gönderim testi. Sadece Development ortamında ve <c>Admin.Diagnostics</c>
    /// yetkisine sahip oturumlu kullanıcılar tarafından çağrılabilir.
    /// HtmlBody doluysa HTML, yoksa Body üzerinden düz metin gönderir.
    /// </summary>
    [HttpPost("send")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Send([FromBody] SendEmailRequest? req, CancellationToken ct)
    {
        if (!_environment.IsDevelopment())
        {
            // Üretimde uç tamamen kapalı; Admin.Diagnostics yetkisine sahip olunsa bile varlığı gizlenir.
            return NotFound();
        }

        if (req is null)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request",
                detail: "Geçersiz istek gövdesi."
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
            return Ok(new { message = "E-posta gönderildi." });
        }
        catch (Exception ex)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Email send failed",
                detail: ex.Message
            );
        }
    }
}
