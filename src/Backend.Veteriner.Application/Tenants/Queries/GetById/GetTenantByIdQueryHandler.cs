using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetById;

public sealed class GetTenantByIdQueryHandler : IRequestHandler<GetTenantByIdQuery, Result<TenantDetailDto>>
{
    private readonly IReadRepository<Tenant> _tenants;

    public GetTenantByIdQueryHandler(IReadRepository<Tenant> tenants) => _tenants = tenants;

    public async Task<Result<TenantDetailDto>> Handle(GetTenantByIdQuery request, CancellationToken ct)
    {
        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(request.Id), ct);
        if (tenant is null)
            return Result<TenantDetailDto>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        var dto = new TenantDetailDto(tenant.Id, tenant.Name, tenant.IsActive, tenant.CreatedAtUtc);
        return Result<TenantDetailDto>.Success(dto);
    }
}
