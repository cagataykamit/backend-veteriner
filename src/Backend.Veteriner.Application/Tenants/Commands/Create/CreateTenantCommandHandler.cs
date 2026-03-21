using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.Create;

public sealed class CreateTenantCommandHandler : IRequestHandler<CreateTenantCommand, Result<Guid>>
{
    private readonly IReadRepository<Tenant> _tenantsRead;
    private readonly IRepository<Tenant> _tenantsWrite;

    public CreateTenantCommandHandler(IReadRepository<Tenant> tenantsRead, IRepository<Tenant> tenantsWrite)
    {
        _tenantsRead = tenantsRead;
        _tenantsWrite = tenantsWrite;
    }

    public async Task<Result<Guid>> Handle(CreateTenantCommand request, CancellationToken ct)
    {
        var nameKey = request.Name.Trim().ToLowerInvariant();
        var duplicate = await _tenantsRead.FirstOrDefaultAsync(new TenantByNameCaseInsensitiveSpec(nameKey), ct);
        if (duplicate is not null)
            return Result<Guid>.Failure(
                "Tenants.DuplicateName",
                "Aynı ada sahip bir kiracı zaten var (büyük/küçük harf ayrımı yapılmaz).");

        var tenant = new Tenant(request.Name);
        await _tenantsWrite.AddAsync(tenant, ct);
        await _tenantsWrite.SaveChangesAsync(ct);
        return Result<Guid>.Success(tenant.Id);
    }
}
