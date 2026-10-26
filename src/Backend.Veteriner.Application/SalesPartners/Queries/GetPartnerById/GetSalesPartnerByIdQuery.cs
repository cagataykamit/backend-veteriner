using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Application.SalesPartners.Specs;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.SalesPartners.Queries.GetPartnerById;

/// <summary>Platform yöneticisi: ortak, özet ve kodları.</summary>
public sealed record GetSalesPartnerByIdQuery(Guid Id) : IRequest<Result<SalesPartnerDetailDto>>;

public sealed class GetSalesPartnerByIdQueryHandler : IRequestHandler<GetSalesPartnerByIdQuery, Result<SalesPartnerDetailDto>>
{
    private readonly IReadRepository<SalesPartner> _partners;
    private readonly IReadRepository<DiscountCode> _codes;
    private readonly IReadRepository<DiscountRedemption> _redemptions;

    public GetSalesPartnerByIdQueryHandler(
        IReadRepository<SalesPartner> partners,
        IReadRepository<DiscountCode> codes,
        IReadRepository<DiscountRedemption> redemptions)
    {
        _partners = partners;
        _codes = codes;
        _redemptions = redemptions;
    }

    public async ValueTask<Result<SalesPartnerDetailDto>> Handle(GetSalesPartnerByIdQuery request, CancellationToken ct)
    {
        var partner = await _partners.FirstOrDefaultAsync(new SalesPartnerByIdSpec(request.Id), ct);
        if (partner is null)
            return Result<SalesPartnerDetailDto>.Failure(SalesPartnerErrors.PartnerNotFound);

        var codes = await _codes.ListAsync(new DiscountCodesByPartnerSpec(partner.Id), ct);
        var redemptions = await _redemptions.ListAsync(new DiscountRedemptionsByPartnerSpec(partner.Id), ct);
        var usage = redemptions.GroupBy(r => r.DiscountCodeId).ToDictionary(g => g.Key, g => g.Count());

        return Result<SalesPartnerDetailDto>.Success(new SalesPartnerDetailDto(
            SalesPartnerDto.From(partner, codes.Count, redemptions),
            codes.Select(c => DiscountCodeDto.From(c, usage.GetValueOrDefault(c.Id))).ToList()));
    }
}
