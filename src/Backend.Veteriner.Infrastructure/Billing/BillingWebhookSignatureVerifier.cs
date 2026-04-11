using System.Diagnostics;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Backend.Veteriner.Infrastructure.Billing;

public sealed class BillingWebhookSignatureVerifier : IBillingWebhookSignatureVerifier
{
    private readonly BillingOptions _options;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IClientContext _clientContext;
    private readonly ILogger<BillingWebhookSignatureVerifier> _logger;

    public BillingWebhookSignatureVerifier(
        IOptions<BillingOptions> options,
        IHostEnvironment hostEnvironment,
        IClientContext clientContext,
        ILogger<BillingWebhookSignatureVerifier> logger)
    {
        _options = options.Value;
        _hostEnvironment = hostEnvironment;
        _clientContext = clientContext;
        _logger = logger;
    }

    public Result Verify(BillingProvider provider, string rawBody, IReadOnlyDictionary<string, string> headers)
    {
        _ = TryGetHeader(headers, "Stripe-Signature", out var stripeSignature);
        if (provider == BillingProvider.Stripe && string.IsNullOrWhiteSpace(stripeSignature))
            return Result.Failure("Billing.WebhookSignatureMissing", "Stripe-Signature başlığı yok.");

        _ = TryGetHeader(headers, "X-IYZ-SIGNATURE-V3", out var iyzicoSignature);

        if (provider == BillingProvider.Iyzico)
        {
            if (string.IsNullOrWhiteSpace(iyzicoSignature))
            {
                if (ShouldAcceptUnsignedIyzicoSandboxWebhook())
                {
                    var traceId = Activity.Current?.Id;
                    var correlationId = _clientContext.CorrelationId;
                    _logger.LogWarning(
                        "unsigned sandbox webhook accepted. Provider={Provider} Environment={Environment} TraceId={TraceId} CorrelationId={CorrelationId}",
                        BillingProvider.Iyzico.ToString(),
                        _hostEnvironment.EnvironmentName,
                        traceId ?? "(none)",
                        string.IsNullOrWhiteSpace(correlationId) ? "(none)" : correlationId);
                    return Result.Success();
                }

                return Result.Failure("Billing.WebhookSignatureMissing", "X-IYZ-SIGNATURE-V3 başlığı yok.");
            }

            return VerifyIyzico(rawBody, iyzicoSignature);
        }

        return provider switch
        {
            BillingProvider.Stripe => VerifyStripe(rawBody, stripeSignature!),
            BillingProvider.Manual or BillingProvider.None => Result.Failure(
                "Billing.WebhookProviderInvalid",
                "Bu provider için webhook desteklenmiyor."),
            _ => Result.Failure("Billing.WebhookProviderInvalid", "Geçersiz billing provider."),
        };
    }

    private bool ShouldAcceptUnsignedIyzicoSandboxWebhook()
    {
        if (!_hostEnvironment.IsDevelopment())
            return false;
        if (!_options.Iyzico.AllowUnsignedSandboxWebhooks)
            return false;
        return IsIyzicoSandboxBaseUrl(_options.Iyzico.BaseUrl);
    }

    private static bool IsIyzicoSandboxBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return false;

        var trimmed = baseUrl.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return string.Equals(uri.Host, "sandbox-api.iyzipay.com", StringComparison.OrdinalIgnoreCase);

        return trimmed.Contains("sandbox-api.iyzipay.com", StringComparison.OrdinalIgnoreCase);
    }

    private Result VerifyStripe(string rawBody, string stripeSignature)
    {
        var secret = _options.Stripe.WebhookSecret?.Trim() ?? "";
        if (string.IsNullOrEmpty(secret))
        {
            return Result.Failure(
                "Billing.StripeWebhookNotConfigured",
                "Stripe webhook imza doğrulaması için Billing:Stripe:WebhookSecret yapılandırılmalı.");
        }

        try
        {
            const int toleranceSeconds = 300;
            EventUtility.ConstructEvent(rawBody, stripeSignature, secret, toleranceSeconds, throwOnApiVersionMismatch: false);
            return Result.Success();
        }
        catch (StripeException ex)
        {
            return Result.Failure("Billing.WebhookSignatureInvalid", ex.Message);
        }
    }

    private Result VerifyIyzico(string rawBody, string providedSignature)
    {
        var iyzico = _options.Iyzico;
        var secret = string.IsNullOrWhiteSpace(iyzico.WebhookSecret) ? iyzico.SecretKey : iyzico.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            return Result.Failure(
                "Billing.IyzicoWebhookNotConfigured",
                "Iyzico webhook doğrulaması için Billing:Iyzico:WebhookSecret (veya SecretKey) yapılandırılmalı.");
        }

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            var expected = BuildExpectedIyzicoSignatures(root, secret.Trim(), iyzico.MerchantId?.Trim() ?? "");
            if (expected.Count == 0)
            {
                return Result.Failure(
                    "Billing.WebhookPayloadInvalid",
                    "Iyzico webhook payload imza doğrulaması için gerekli alanları içermiyor.");
            }

            foreach (var candidate in expected)
            {
                if (string.Equals(candidate, providedSignature, StringComparison.OrdinalIgnoreCase))
                    return Result.Success();
            }

            return Result.Failure("Billing.WebhookSignatureInvalid", "Iyzico webhook imza doğrulaması başarısız.");
        }
        catch (Exception ex)
        {
            return Result.Failure("Billing.WebhookPayloadInvalid", $"Iyzico webhook parse/doğrulama hatası: {ex.Message}");
        }
    }

    private static List<string> BuildExpectedIyzicoSignatures(JsonElement root, string secretKey, string merchantId)
    {
        var list = new List<string>();
        var iyziEventType = GetWebhookConcatPart(root, "iyziEventType")
                            ?? GetWebhookConcatPart(root, "eventType")
                            ?? "";

        // HPP format: secret + iyziEventType + iyziPaymentId + token + paymentConversationId + status
        // Docs: iyziPaymentId is long in JSON — must not use GetString() on numeric tokens.
        var iyziPaymentId = GetWebhookConcatPart(root, "iyziPaymentId") ?? "";
        var token = GetWebhookConcatPart(root, "token") ?? "";
        var paymentConversationId = GetWebhookConcatPart(root, "paymentConversationId") ?? "";
        var status = GetWebhookConcatPart(root, "status") ?? GetWebhookConcatPart(root, "paymentStatus") ?? "";
        if (!string.IsNullOrEmpty(iyziEventType) && !string.IsNullOrEmpty(paymentConversationId) && !string.IsNullOrEmpty(status))
        {
            var hppMessage = secretKey + iyziEventType + iyziPaymentId + token + paymentConversationId + status;
            list.Add(ComputeHmacSha256Hex(secretKey, hppMessage));
        }

        // Direct format: secret + iyziEventType + paymentId + paymentConversationId + status
        var paymentId = GetWebhookConcatPart(root, "paymentId") ?? "";
        if (!string.IsNullOrEmpty(iyziEventType) && !string.IsNullOrEmpty(paymentConversationId) && !string.IsNullOrEmpty(status))
        {
            var directMessage = secretKey + iyziEventType + paymentId + paymentConversationId + status;
            list.Add(ComputeHmacSha256Hex(secretKey, directMessage));
        }

        // Subscription format: merchantId + secret + eventType + subscriptionReferenceCode + orderReferenceCode + customerReferenceCode
        var subscriptionReferenceCode = GetWebhookConcatPart(root, "subscriptionReferenceCode") ?? "";
        var orderReferenceCode = GetWebhookConcatPart(root, "orderReferenceCode") ?? "";
        var customerReferenceCode = GetWebhookConcatPart(root, "customerReferenceCode") ?? "";
        if (!string.IsNullOrEmpty(merchantId)
            && !string.IsNullOrEmpty(iyziEventType)
            && !string.IsNullOrEmpty(subscriptionReferenceCode)
            && !string.IsNullOrEmpty(orderReferenceCode)
            && !string.IsNullOrEmpty(customerReferenceCode))
        {
            var subMessage = merchantId + secretKey + iyziEventType + subscriptionReferenceCode + orderReferenceCode + customerReferenceCode;
            list.Add(ComputeHmacSha256Hex(secretKey, subMessage));
        }

        return list;
    }

    private static string ComputeHmacSha256Hex(string secretKey, string message)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// iyzico webhook imza metninde kullanılan alanlar string veya sayı (ör. iyziPaymentId) olabilir.
    /// </summary>
    private static string? GetWebhookConcatPart(JsonElement root, string prop)
    {
        if (!TryGetPropertyInsensitive(root, prop, out var v))
            return null;

        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => null,
        };
    }

    private static bool TryGetPropertyInsensitive(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetHeader(IReadOnlyDictionary<string, string> headers, string name, out string value)
    {
        foreach (var kv in headers)
        {
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = kv.Value;
                return !string.IsNullOrWhiteSpace(value);
            }
        }

        value = "";
        return false;
    }
}
