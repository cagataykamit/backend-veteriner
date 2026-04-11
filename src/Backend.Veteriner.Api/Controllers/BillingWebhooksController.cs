using System.Text;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Tenants.Commands.Billing;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Ödeme sağlayıcı webhook uçları. İmza doğrulaması uygulama katmanında yapılır; JWT gerekmez.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/webhooks/billing")]
[Produces("application/json")]
[AllowAnonymous]
[DisableRateLimiting]
public sealed class BillingWebhooksController : ControllerBase
{
    private readonly IMediator _mediator;

    public BillingWebhooksController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Stripe Billing webhook (ham gövde imza için olduğu gibi okunur).</summary>
    [HttpPost("stripe")]
    [Consumes("application/json", "text/plain")]
    public async Task<IActionResult> PostStripe(CancellationToken ct)
    {
        var raw = await ReadRawBodyAsync(ct);
        var headers = CopyHeaders();
        var result = await _mediator.Send(new ProcessBillingWebhookCommand(BillingProvider.Stripe, raw, headers), ct);
        return ToWebhookResult(result);
    }

    /// <summary>İyzico webhook (imza/parse henüz tamamlanmadığında 503/400 dönebilir).</summary>
    [HttpPost("iyzico")]
    [Consumes("application/json", "text/plain")]
    public async Task<IActionResult> PostIyzico(CancellationToken ct)
    {
        var raw = await ReadRawBodyAsync(ct);
        var headers = CopyHeaders();
        var result = await _mediator.Send(new ProcessBillingWebhookCommand(BillingProvider.Iyzico, raw, headers), ct);
        return ToWebhookResult(result);
    }

    private async Task<string> ReadRawBodyAsync(CancellationToken ct)
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        var body = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;
        return body;
    }

    private Dictionary<string, string> CopyHeaders()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in Request.Headers)
            headers[header.Key] = header.Value.ToString();
        return headers;
    }

    private IActionResult ToWebhookResult(Result<BillingWebhookAckDto> result)
        => result.ToActionResult(this);
}
