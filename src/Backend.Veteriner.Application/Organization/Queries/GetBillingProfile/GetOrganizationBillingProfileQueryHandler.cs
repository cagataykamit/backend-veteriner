using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Organization.Contracts.Dtos;
using Backend.Veteriner.Application.Organization.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Organization.Queries.GetBillingProfile;

public sealed class GetOrganizationBillingProfileQueryHandler
    : IRequestHandler<GetOrganizationBillingProfileQuery, Result<OrganizationBillingProfileDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IReadRepository<TenantBillingProfile> _profilesRead;

    public GetOrganizationBillingProfileQueryHandler(
        ITenantContext tenantContext,
        ICurrentUserPermissionChecker permissions,
        IReadRepository<TenantBillingProfile> profilesRead)
    {
        _tenantContext = tenantContext;
        _permissions = permissions;
        _profilesRead = profilesRead;
    }

    public async Task<Result<OrganizationBillingProfileDto>> Handle(
        GetOrganizationBillingProfileQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<OrganizationBillingProfileDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        var canRead = _permissions.HasPermission(PermissionCatalog.Tenants.Read)
            || _permissions.HasPermission(PermissionCatalog.Tenants.InviteCreate);
        if (!canRead)
        {
            return Result<OrganizationBillingProfileDto>.Failure(
                "Auth.PermissionDenied",
                "Fatura profilini okumak için Tenants.Read veya Tenants.InviteCreate yetkisi gerekir.");
        }

        var row = await _profilesRead.FirstOrDefaultAsync(new TenantBillingProfileByTenantSpec(tenantId), ct);
        if (row is null)
            return Result<OrganizationBillingProfileDto>.Success(EmptyDto());

        return Result<OrganizationBillingProfileDto>.Success(Map(row));
    }

    private static OrganizationBillingProfileDto EmptyDto()
        => new(null, null, null, null, null, null, null, null, null, null, null, null);

    private static OrganizationBillingProfileDto Map(TenantBillingProfile row)
        => new(
            row.CompanyName,
            row.LegalCompanyName,
            row.TaxOffice,
            row.TaxNumber,
            row.CompanyPhone,
            row.InvoiceProvince,
            row.InvoiceDistrict,
            row.InvoiceNeighborhood,
            row.InvoiceStreet,
            row.InvoiceBuildingName,
            row.InvoiceBuildingNo,
            row.InvoiceDoorNo);
}
