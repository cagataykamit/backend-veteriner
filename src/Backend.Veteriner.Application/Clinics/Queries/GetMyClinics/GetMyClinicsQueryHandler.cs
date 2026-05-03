using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Queries.GetMyClinics;

public sealed class GetMyClinicsQueryHandler : IRequestHandler<GetMyClinicsQuery, Result<IReadOnlyList<ClinicListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _client;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IUserTenantRepository _userTenants;
    private readonly IUserClinicRepository _userClinics;

    public GetMyClinicsQueryHandler(
        ITenantContext tenantContext,
        IClientContext client,
        IReadRepository<Tenant> tenants,
        IUserTenantRepository userTenants,
        IUserClinicRepository userClinics)
    {
        _tenantContext = tenantContext;
        _client = client;
        _tenants = tenants;
        _userTenants = userTenants;
        _userClinics = userClinics;
    }

    public async Task<Result<IReadOnlyList<ClinicListItemDto>>> Handle(GetMyClinicsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<IReadOnlyList<ClinicListItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var userId = _client.UserId;
        if (userId is null)
        {
            return Result<IReadOnlyList<ClinicListItemDto>>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<IReadOnlyList<ClinicListItemDto>>.Failure("Tenants.NotFound", "Tenant bulunamadı.");
        if (!tenant.IsActive)
            return Result<IReadOnlyList<ClinicListItemDto>>.Failure("Tenants.TenantInactive", "Pasif kiracı için klinik listelenemez.");

        if (!await _userTenants.ExistsAsync(userId.Value, tenantId, ct))
        {
            return Result<IReadOnlyList<ClinicListItemDto>>.Failure(
                "Auth.TenantNotMember",
                "Bu kiracıda üyeliğiniz yok.");
        }

        var rows = await _userClinics.ListAccessibleClinicsAsync(userId.Value, tenantId, request.IsActive, ct);

        var dtos = rows
            .Select(c => new ClinicListItemDto(c.Id, c.TenantId, c.Name, c.City, c.IsActive, c.Phone, c.Email))
            .ToArray();

        return Result<IReadOnlyList<ClinicListItemDto>>.Success(dtos);
    }
}

