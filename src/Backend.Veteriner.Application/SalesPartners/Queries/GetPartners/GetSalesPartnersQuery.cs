using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Application.SalesPartners.Specs;
using Backend.Veteriner.Domain.SalesPartners;
using MediatR;

namespace Backend.Veteriner.Application.SalesPartners.Queries.GetPartners;

/// <summary>Platform yöneticisi: tüm ortaklar ve ortak başına özet (ilk sürümde ortak sayısı küçük; sayfalama yok).</summary>
public sealed record GetSalesPartnersQuery : IRequest<IReadOnlyList<SalesPartnerDto>>;

public sealed class GetSalesPartnersQueryHandler : IRequestHandler<GetSalesPartnersQuery, IReadOnlyList<SalesPartnerDto>>
{
    private readonly IReadRepository<SalesPartner> _partners;
    private readonly IReadRepository<DiscountCode> _codes;
    private readonly IReadRepository<DiscountRedemption> _redemptions;

    public GetSalesPartnersQueryHandler(
        IReadRepository<SalesPartner> partners,
        IReadRepository<DiscountCode> codes,
        IReadRepository<DiscountRedemption> redemptions)
    {
        _partners = partners;
        _codes = codes;
        _redemptions = redemptions;
    }

    public async Task<IReadOnlyList<SalesPartnerDto>> Handle(GetSalesPartnersQuery request, CancellationToken ct)
    {
        var partners = await _partners.ListAsync(new SalesPartnersOrderedSpec(), ct);
        var codes = await _codes.ListAsync(new DiscountCodesByPartnerSpec(null), ct);
        var redemptions = await _redemptions.ListAsync(new DiscountRedemptionsByPartnerSpec(null), ct);

        var codeCounts = codes.GroupBy(c => c.SalesPartnerId).ToDictionary(g => g.Key, g => g.Count());
        var byPartner = redemptions.GroupBy(r => r.SalesPartnerId).ToDictionary(g => g.Key, g => (IReadOnlyCollection<DiscountRedemption>)g.ToList());

        return partners
            .Select(p => SalesPartnerDto.From(
                p,
                codeCounts.GetValueOrDefault(p.Id),
                byPartner.GetValueOrDefault(p.Id) ?? []))
            .ToList();
    }
}
