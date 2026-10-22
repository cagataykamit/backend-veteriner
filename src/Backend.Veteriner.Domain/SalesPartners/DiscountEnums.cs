namespace Backend.Veteriner.Domain.SalesPartners;

/// <summary>Tahsilatın kapsadığı faturalandırma dönemi (ADR-037 madde 3-4: aylıkta 12 ay penceresi, yıllıkta tek tahsilat).</summary>
public enum BillingPeriodKind
{
    Monthly = 1,
    Yearly = 2,
}

/// <summary>Tahsilat satırının komisyon durumu: Tahakkuk, İptal (iade/iptal; komisyon geri alınır), Ödendi.</summary>
public enum CommissionStatus
{
    Accrued = 1,
    Cancelled = 2,
    Paid = 3,
}
