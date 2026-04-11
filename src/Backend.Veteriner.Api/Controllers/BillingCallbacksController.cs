using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.Common.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/billing")]
[AllowAnonymous]
[DisableRateLimiting]
public sealed class BillingCallbacksController : ControllerBase
{
    private readonly BillingOptions _billingOptions;
    private readonly ILogger<BillingCallbacksController> _logger;

    public BillingCallbacksController(
        IOptions<BillingOptions> billingOptions,
        ILogger<BillingCallbacksController> logger)
    {
        _billingOptions = billingOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Iyzico hosted checkout dönüş endpoint'i.
    /// Webhook-first mimaride callback yalnız redirect bridge olarak kullanılır.
    /// Aktivasyon source-of-truth: webhook hattıdır.
    /// </summary>
    [HttpPost("iyzico/callback")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> PostIyzicoCallback([FromForm] IyzicoCallbackForm form, CancellationToken ct)
    {
        var callbackForm = await Request.ReadFormAsync(ct);
        var token = ResolveCallbackToken(callbackForm, form);
        var conversationHint = ResolveConversationHint(callbackForm, form);
        var checkoutSessionIdHint = Guid.TryParse(conversationHint, out var parsedHint) ? parsedHint : (Guid?)null;

        _logger.LogInformation(
            "Iyzico callback bridge alındı (webhook-first). TokenHint={TokenHint}, ConversationHint={ConversationHint}, FormKeys={FormKeys}",
            ToTokenHint(token),
            ToHint(conversationHint),
            string.Join(",", callbackForm.Keys));

        return Redirect(BuildProcessingReturnUrl(checkoutSessionIdHint));
    }

    private string BuildProcessingReturnUrl(Guid? checkoutSessionId)
    {
        var iyzico = _billingOptions.Iyzico;
        var baseUrl = iyzico.ReturnSuccessUrl;

        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = "http://localhost:4200/panel/settings/subscription";

        var separator = baseUrl.Contains('?') ? "&" : "?";
        var url = $"{baseUrl}{separator}checkout=processing&provider=iyzico";
        if (checkoutSessionId.HasValue)
            url += $"&checkoutSessionId={checkoutSessionId.Value:D}";

        return url;
    }

    public sealed class IyzicoCallbackForm
    {
        public string? Token { get; init; }
        public string? ConversationId { get; init; }
        public string? ConversationData { get; init; }
    }

    private static string? ResolveCallbackToken(IFormCollection formValues, IyzicoCallbackForm bound)
    {
        var candidates = new[]
        {
            bound.Token,
            formValues.TryGetValue("token", out var token) ? token.ToString() : null,
            formValues.TryGetValue("Token", out var tokenPascal) ? tokenPascal.ToString() : null,
            formValues.TryGetValue("checkoutFormToken", out var checkoutToken) ? checkoutToken.ToString() : null,
            formValues.TryGetValue("checkoutToken", out var checkoutTokenAlt) ? checkoutTokenAlt.ToString() : null,
        };

        foreach (var candidate in candidates)
        {
            var normalized = NormalizeCallbackTokenCandidate(candidate);
            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized;
        }

        return null;
    }

    private static string? NormalizeCallbackTokenCandidate(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        var value = candidate.Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            value = Uri.UnescapeDataString(value);
        }
        catch
        {
            // ignore decode errors, raw value ile devam et.
        }

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? ResolveConversationHint(IFormCollection formValues, IyzicoCallbackForm bound)
    {
        var candidates = new[]
        {
            bound.ConversationId,
            bound.ConversationData,
            formValues.TryGetValue("conversationId", out var convId) ? convId.ToString() : null,
            formValues.TryGetValue("conversationData", out var convData) ? convData.ToString() : null,
            formValues.TryGetValue("paymentConversationId", out var paymentConvId) ? paymentConvId.ToString() : null,
            formValues.TryGetValue("basketId", out var basketId) ? basketId.ToString() : null,
        };

        foreach (var candidate in candidates)
        {
            var trimmed = candidate?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                return trimmed;
        }

        return null;
    }

    private static string ToTokenHint(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return "(empty)";
        var t = token.Trim();
        var head = t.Length <= 6 ? t : t[..6];
        return $"{head}...len:{t.Length}";
    }

    private static string ToHint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "(empty)";
        var v = value.Trim();
        return v.Length <= 12 ? v : $"{v[..12]}...";
    }
}
