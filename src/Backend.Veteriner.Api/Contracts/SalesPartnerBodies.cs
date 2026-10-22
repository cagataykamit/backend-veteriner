namespace Backend.Veteriner.Api.Contracts;

/// <summary>Satış ortağı ve indirim kodu uç gövdeleri (ADR-037, <c>docs/SALES_PARTNER_DISCOUNT_API_CONTRACT.md</c>).</summary>
public sealed class DiscountCodeBody
{
    public string? Code { get; set; }
}

public sealed class SalesPartnerBody
{
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public Guid? LinkedTenantId { get; set; }

    /// <summary>Yalnız güncellemede; oluşturmada yok sayılır (aktif doğar).</summary>
    public bool IsActive { get; set; } = true;
}

public sealed class DiscountCodeTermsBody
{
    /// <summary>Yalnız oluşturmada; kod metni sonradan değişmez.</summary>
    public string? Code { get; set; }

    public decimal DiscountPercent { get; set; }
    public decimal CommissionPercent { get; set; }
    public DateTime? ValidFromUtc { get; set; }
    public DateTime? ValidUntilUtc { get; set; }
    public int? MaxRedemptions { get; set; }
}

public sealed class MarkCommissionsPaidBody
{
    public List<Guid> CollectionIds { get; set; } = [];
    public DateTime PaidAtUtc { get; set; }
    public string? Note { get; set; }
}

public sealed class CancelDiscountCollectionBody
{
    public string Reason { get; set; } = string.Empty;
}
