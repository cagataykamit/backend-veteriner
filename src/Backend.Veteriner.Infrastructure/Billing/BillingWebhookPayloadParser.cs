using System.Text.Json;
using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Infrastructure.Billing;

public sealed class BillingWebhookPayloadParser : IBillingWebhookPayloadParser
{
    public Result<BillingWebhookNormalizedEvent> Parse(BillingProvider provider, string rawBody)
    {
        return provider switch
        {
            BillingProvider.Stripe => ParseStripe(rawBody),
            BillingProvider.Iyzico => ParseIyzico(rawBody),
            _ => Result<BillingWebhookNormalizedEvent>.Failure(
                "Billing.WebhookPayloadUnsupported",
                "Bu provider için webhook payload işlenemez."),
        };
    }

    private static Result<BillingWebhookNormalizedEvent> ParseStripe(string rawBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            var id = root.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(id))
                return Result<BillingWebhookNormalizedEvent>.Failure("Billing.WebhookPayloadInvalid", "Stripe event id yok.");

            var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

            var kind = MapStripeEventKind(type);
            Guid? checkoutSessionId = null;
            string? providerPaymentRef = null;

            if (root.TryGetProperty("data", out var data) && data.TryGetProperty("object", out var obj))
            {
                if (obj.TryGetProperty("id", out var objId))
                    providerPaymentRef = objId.GetString();

                if (obj.TryGetProperty("metadata", out var meta)
                    && meta.TryGetProperty("billing_checkout_session_id", out var metaSession)
                    && Guid.TryParse(metaSession.GetString(), out var parsedSession))
                {
                    checkoutSessionId = parsedSession;
                }
            }

            return Result<BillingWebhookNormalizedEvent>.Success(
                new BillingWebhookNormalizedEvent(id, type, kind, checkoutSessionId, providerPaymentRef));
        }
        catch (Exception ex)
        {
            return Result<BillingWebhookNormalizedEvent>.Failure(
                "Billing.WebhookPayloadInvalid",
                $"Stripe webhook JSON ayrıştırılamadı: {ex.Message}");
        }
    }

    private static BillingWebhookEventKind MapStripeEventKind(string? type)
    {
        return type switch
        {
            "checkout.session.completed" => BillingWebhookEventKind.PaymentSucceeded,
            "checkout.session.async_payment_failed" => BillingWebhookEventKind.PaymentFailed,
            _ => BillingWebhookEventKind.Ignored,
        };
    }

    private static Result<BillingWebhookNormalizedEvent> ParseIyzico(string rawBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            var providerEventId = GetStringInsensitive(root, "iyziReferenceCode")
                                  ?? GetTextOrNumberInsensitive(root, "iyziPaymentId")
                                  ?? GetTextOrNumberInsensitive(root, "paymentId")
                                  ?? GetStringInsensitive(root, "orderReferenceCode");
            if (string.IsNullOrWhiteSpace(providerEventId))
            {
                return Result<BillingWebhookNormalizedEvent>.Failure(
                    "Billing.WebhookPayloadInvalid",
                    "Iyzico webhook event kimliği bulunamadı.");
            }

            var eventType = GetStringInsensitive(root, "iyziEventType") ?? GetStringInsensitive(root, "eventType");
            var status = ResolveIyzicoPaymentStatus(root);
            // Checkout Form (HPP): conversation id = paymentConversationId; BasketId aynı GUID string (bkz. IyzicoBillingCheckoutProvider).
            var checkoutSessionId = TryParseGuid(GetStringInsensitive(root, "paymentConversationId"))
                                    ?? TryParseGuid(GetStringInsensitive(root, "basketId"))
                                    ?? TryParseGuid(GetStringInsensitive(root, "conversationId"));

            var kind = MapIyzicoKind(eventType, status);
            var providerPaymentReference = GetTextOrNumberInsensitive(root, "iyziPaymentId")
                                           ?? GetTextOrNumberInsensitive(root, "paymentId")
                                           ?? GetStringInsensitive(root, "token")
                                           ?? GetStringInsensitive(root, "subscriptionReferenceCode");

            return Result<BillingWebhookNormalizedEvent>.Success(
                new BillingWebhookNormalizedEvent(
                    providerEventId.Trim(),
                    eventType,
                    kind,
                    checkoutSessionId,
                    providerPaymentReference));
        }
        catch (Exception ex)
        {
            return Result<BillingWebhookNormalizedEvent>.Failure(
                "Billing.WebhookPayloadInvalid",
                $"Iyzico webhook JSON ayrıştırılamadı: {ex.Message}");
        }
    }

    private static BillingWebhookEventKind MapIyzicoKind(string? eventType, string? status)
    {
        var normalizedEvent = eventType?.Trim().ToLowerInvariant() ?? "";
        var normalizedStatus = NormalizeIyzicoStatus(status);

        if (normalizedEvent == "subscription.order.success")
            return BillingWebhookEventKind.PaymentSucceeded;
        if (normalizedEvent == "subscription.order.failure")
            return BillingWebhookEventKind.PaymentFailed;

        // HPP (Checkout Form): iyziEventType adımı; nihai sonuç status ile gelir (iyzico dokümantasyonu).
        if (normalizedEvent == "checkout_form_auth")
        {
            if (normalizedStatus == "SUCCESS")
                return BillingWebhookEventKind.PaymentSucceeded;
            if (normalizedStatus == "FAILURE")
                return BillingWebhookEventKind.PaymentFailed;
        }

        return normalizedStatus switch
        {
            "SUCCESS" => BillingWebhookEventKind.PaymentSucceeded,
            "FAILURE" => BillingWebhookEventKind.PaymentFailed,
            _ => BillingWebhookEventKind.Ignored,
        };
    }

    /// <summary>status alanı bazen farklı isimle veya string dışı token ile gelebilir.</summary>
    private static string? ResolveIyzicoPaymentStatus(JsonElement root)
        => GetStringInsensitive(root, "status")
           ?? GetStringInsensitive(root, "paymentStatus");

    private static string NormalizeIyzicoStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "";
        return status.Trim().ToUpperInvariant();
    }

    private static Guid? TryParseGuid(string? value)
        => Guid.TryParse(value, out var id) ? id : null;

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

    private static string? GetStringInsensitive(JsonElement root, string prop)
    {
        if (!TryGetPropertyInsensitive(root, prop, out var v))
            return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static string? GetTextOrNumberInsensitive(JsonElement root, string prop)
    {
        if (!TryGetPropertyInsensitive(root, prop, out var v))
            return null;

        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
    }
}
