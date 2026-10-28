namespace Backend.Veteriner.Domain.Tenants;

/// <summary>
/// Çift ödeme iadesi (BILL-005): aynı ödeme denemesinde ikinci ödeme paketi açmaz, sağlayıcıda iptal/iade edilir.
/// </summary>
public enum BillingCheckoutRefundStatus
{
    None = 0,
    Pending = 1,
    Refunded = 2,
    ManualRequired = 3,
}
