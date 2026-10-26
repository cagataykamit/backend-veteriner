using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Application.SalesPartners.Specs;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;

namespace Backend.Veteriner.Application.SalesPartners.Queries.GetRedemptions;

/// <summary>Platform yöneticisi: ortağın kod kullanımları (klinik adıyla) ve tahsilat satırları.</summary>
public sealed record GetSalesPartnerRedemptionsQuery(Guid SalesPartnerId) : IRequest<Result<IReadOnlyList<DiscountRedemptionRowDto>>>;

public sealed class GetSalesPartnerRedemptionsQueryHandler
    : IRequestHandler<GetSalesPartnerRedemptionsQuery, Result<IReadOnlyList<DiscountRedemptionRowDto>>>
{
    private readonly IReadRepository<SalesPartner> _partners;
    private readonly IReadRepository<DiscountRedemption> _redemptions;
    private readonly IReadRepository<Tenant> _tenants;

    public GetSalesPartnerRedemptionsQueryHandler(
        IReadRepository<SalesPartner> partners,
        IReadRepository<DiscountRedemption> redemptions,
        IReadRepository<Tenant> tenants)
    {
        _partners = partners;
        _redemptions = redemptions;
        _tenants = tenants;
    }

    public async ValueTask<Result<IReadOnlyList<DiscountRedemptionRowDto>>> Handle(GetSalesPartnerRedemptionsQuery request, CancellationToken ct)
    {
        if (!await _partners.AnyAsync(new SalesPartnerByIdSpec(request.SalesPartnerId), ct))
            return Result<IReadOnlyList<DiscountRedemptionRowDto>>.Failure(SalesPartnerErrors.PartnerNotFound);

        var redemptions = await _redemptions.ListAsync(new DiscountRedemptionsByPartnerSpec(request.SalesPartnerId), ct);
        var names = await SalesPartnerTenantNames.LoadAsync(_tenants, redemptions.Select(r => r.TenantId), ct);

        IReadOnlyList<DiscountRedemptionRowDto> rows = redemptions
            .Select(r => DiscountRedemptionRowDto.From(r, names.GetValueOrDefault(r.TenantId)))
            .ToList();
        return Result<IReadOnlyList<DiscountRedemptionRowDto>>.Success(rows);
    }
}
