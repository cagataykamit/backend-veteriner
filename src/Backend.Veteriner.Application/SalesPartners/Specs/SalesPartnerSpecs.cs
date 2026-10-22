using Ardalis.Specification;
using Backend.Veteriner.Domain.SalesPartners;

namespace Backend.Veteriner.Application.SalesPartners.Specs;

public sealed class SalesPartnerByIdSpec : Specification<SalesPartner>, ISingleResultSpecification<SalesPartner>
{
    public SalesPartnerByIdSpec(Guid id) => Query.Where(x => x.Id == id);
}

public sealed class SalesPartnersOrderedSpec : Specification<SalesPartner>
{
    public SalesPartnersOrderedSpec()
    {
        Query.AsNoTracking();
        Query.OrderBy(x => x.Name).ThenBy(x => x.CreatedAtUtc);
    }
}

public sealed class DiscountCodeByIdSpec : Specification<DiscountCode>, ISingleResultSpecification<DiscountCode>
{
    public DiscountCodeByIdSpec(Guid salesPartnerId, Guid id) => Query.Where(x => x.Id == id && x.SalesPartnerId == salesPartnerId);
}

public sealed class DiscountCodeByNormalizedCodeSpec : Specification<DiscountCode>, ISingleResultSpecification<DiscountCode>
{
    public DiscountCodeByNormalizedCodeSpec(string normalizedCode) => Query.Where(x => x.NormalizedCode == normalizedCode);
}

/// <summary>Ortağın kodları (partnerId boşsa tümü), oluşturulma sırasına göre.</summary>
public sealed class DiscountCodesByPartnerSpec : Specification<DiscountCode>
{
    public DiscountCodesByPartnerSpec(Guid? salesPartnerId)
    {
        Query.AsNoTracking();
        if (salesPartnerId.HasValue)
            Query.Where(x => x.SalesPartnerId == salesPartnerId.Value);
        Query.OrderBy(x => x.CreatedAtUtc);
    }
}

/// <summary>Kodun kullanım sayısı (limit kontrolü).</summary>
public sealed class DiscountRedemptionsByCodeSpec : Specification<DiscountRedemption>
{
    public DiscountRedemptionsByCodeSpec(Guid discountCodeId) => Query.Where(x => x.DiscountCodeId == discountCodeId);
}

/// <summary>Kiracının kod kullanımı ve tahsilatları; izlenir (tahsilat yazımı için).</summary>
public sealed class DiscountRedemptionByTenantSpec : Specification<DiscountRedemption>, ISingleResultSpecification<DiscountRedemption>
{
    public DiscountRedemptionByTenantSpec(Guid tenantId)
    {
        Query.Where(x => x.TenantId == tenantId);
        Query.Include(x => x.Collections);
    }
}

/// <summary>Ortağın (boşsa tüm ortakların) kod kullanımları ve tahsilatları; <paramref name="tracked"/> yazma uçları içindir.</summary>
public sealed class DiscountRedemptionsByPartnerSpec : Specification<DiscountRedemption>
{
    public DiscountRedemptionsByPartnerSpec(Guid? salesPartnerId, bool tracked = false)
    {
        if (!tracked)
            Query.AsNoTracking();
        if (salesPartnerId.HasValue)
            Query.Where(x => x.SalesPartnerId == salesPartnerId.Value);
        Query.Include(x => x.Collections);
        Query.OrderByDescending(x => x.RedeemedAtUtc);
    }
}
