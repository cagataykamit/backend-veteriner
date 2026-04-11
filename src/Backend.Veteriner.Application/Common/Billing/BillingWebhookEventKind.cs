namespace Backend.Veteriner.Application.Common.Billing;

public enum BillingWebhookEventKind
{
    Ignored = 0,
    PaymentSucceeded = 1,
    PaymentFailed = 2,
}
