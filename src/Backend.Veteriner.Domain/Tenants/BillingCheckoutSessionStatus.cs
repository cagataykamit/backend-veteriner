namespace Backend.Veteriner.Domain.Tenants;

public enum BillingCheckoutSessionStatus
{
    Pending = 0,
    RedirectReady = 1,
    Completed = 2,
    Failed = 3,
    Expired = 4,
    Cancelled = 5,
}

