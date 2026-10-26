using Backend.Veteriner.Domain.SalesPartners;

namespace Backend.Veteriner.Domain.Tenants;

/// <summary>Ücretli abonelik dönem uzunluğunun tek yeri (BILL-001): aylık 1 ay, yıllık 1 yıl.</summary>
public static class BillingPeriods
{
    public static DateTime EndOf(DateTime periodStartUtc, BillingPeriodKind period)
        => period switch
        {
            BillingPeriodKind.Monthly => periodStartUtc.AddMonths(1),
            BillingPeriodKind.Yearly => periodStartUtc.AddYears(1),
            _ => throw new ArgumentOutOfRangeException(nameof(period)),
        };
}
