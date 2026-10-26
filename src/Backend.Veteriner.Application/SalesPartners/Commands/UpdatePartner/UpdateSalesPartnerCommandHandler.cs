using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Application.SalesPartners.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;

namespace Backend.Veteriner.Application.SalesPartners.Commands.UpdatePartner;

public sealed class UpdateSalesPartnerCommandHandler : IRequestHandler<UpdateSalesPartnerCommand, Result<SalesPartnerDto>>
{
    private readonly IRepository<SalesPartner> _partners;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<DiscountCode> _codes;
    private readonly IReadRepository<DiscountRedemption> _redemptions;

    public UpdateSalesPartnerCommandHandler(
        IRepository<SalesPartner> partners,
        IReadRepository<Tenant> tenants,
        IReadRepository<DiscountCode> codes,
        IReadRepository<DiscountRedemption> redemptions)
    {
        _partners = partners;
        _tenants = tenants;
        _codes = codes;
        _redemptions = redemptions;
    }

    public async ValueTask<Result<SalesPartnerDto>> Handle(UpdateSalesPartnerCommand request, CancellationToken ct)
    {
        var partner = await _partners.FirstOrDefaultAsync(new SalesPartnerByIdSpec(request.Id), ct);
        if (partner is null)
            return Result<SalesPartnerDto>.Failure(SalesPartnerErrors.PartnerNotFound);

        if (request.LinkedTenantId is { } tenantId && !await _tenants.AnyAsync(new TenantByIdSpec(tenantId), ct))
            return Result<SalesPartnerDto>.Failure(SalesPartnerErrors.TenantNotFound);

        partner.Update(request.Name, request.Email, request.Phone, request.LinkedTenantId, request.IsActive, DateTime.UtcNow);
        await _partners.SaveChangesAsync(ct);

        var codeCount = await _codes.CountAsync(new DiscountCodesByPartnerSpec(partner.Id), ct);
        var redemptions = await _redemptions.ListAsync(new DiscountRedemptionsByPartnerSpec(partner.Id), ct);
        return Result<SalesPartnerDto>.Success(SalesPartnerDto.From(partner, codeCount, redemptions));
    }
}
