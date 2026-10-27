using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;

namespace Backend.Veteriner.Application.SalesPartners.Commands.CreatePartner;

public sealed class CreateSalesPartnerCommandHandler : IRequestHandler<CreateSalesPartnerCommand, Result<SalesPartnerDto>>
{
    private readonly IRepository<SalesPartner> _partners;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly TimeProvider _timeProvider;

    public CreateSalesPartnerCommandHandler(IRepository<SalesPartner> partners, IReadRepository<Tenant> tenants, TimeProvider timeProvider)
    {
        _partners = partners;
        _tenants = tenants;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<SalesPartnerDto>> Handle(CreateSalesPartnerCommand request, CancellationToken ct)
    {
        if (request.LinkedTenantId is { } tenantId && !await _tenants.AnyAsync(new TenantByIdSpec(tenantId), ct))
            return Result<SalesPartnerDto>.Failure(SalesPartnerErrors.TenantNotFound);

        var partner = SalesPartner.Create(request.Name, request.Email, request.Phone, request.LinkedTenantId, _timeProvider.GetUtcNow().UtcDateTime);
        await _partners.AddAsync(partner, ct);
        await _partners.SaveChangesAsync(ct);

        return Result<SalesPartnerDto>.Success(SalesPartnerDto.From(partner, 0, []));
    }
}
