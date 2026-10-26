namespace Backend.Veteriner.Api.Contracts;

public sealed class StartSubscriptionCheckoutBody
{
    public string TargetPlanCode { get; set; } = default!;

    /// <summary><c>Monthly</c> | <c>Yearly</c> (BILL-001); boşsa mevcut dönem (ilk ödemede aylık).</summary>
    public string? BillingPeriod { get; set; }
}
