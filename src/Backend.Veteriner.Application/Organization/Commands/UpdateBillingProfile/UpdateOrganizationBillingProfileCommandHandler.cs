using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Organization.Contracts.Dtos;
using Backend.Veteriner.Application.Organization.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Organization.Commands.UpdateBillingProfile;

public sealed class UpdateOrganizationBillingProfileCommandHandler
    : IRequestHandler<UpdateOrganizationBillingProfileCommand, Result<OrganizationBillingProfileDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IReadRepository<TenantBillingProfile> _profilesRead;
    private readonly IRepository<TenantBillingProfile> _profilesWrite;

    public UpdateOrganizationBillingProfileCommandHandler(
        ITenantContext tenantContext,
        ICurrentUserPermissionChecker permissions,
        IReadRepository<TenantBillingProfile> profilesRead,
        IRepository<TenantBillingProfile> profilesWrite)
    {
        _tenantContext = tenantContext;
        _permissions = permissions;
        _profilesRead = profilesRead;
        _profilesWrite = profilesWrite;
    }

    public async Task<Result<OrganizationBillingProfileDto>> Handle(
        UpdateOrganizationBillingProfileCommand request,
        CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Tenants.InviteCreate))
        {
            return Result<OrganizationBillingProfileDto>.Failure(
                "Auth.PermissionDenied",
                "Fatura profilini güncellemek için Tenants.InviteCreate yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<OrganizationBillingProfileDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        var row = await _profilesRead.FirstOrDefaultAsync(new TenantBillingProfileByTenantSpec(tenantId), ct);
        if (row is null)
        {
            row = TenantBillingProfile.CreateEmpty(tenantId);
            row.Update(
                request.CompanyName,
                request.LegalCompanyName,
                request.TaxOffice,
                request.TaxNumber,
                request.CompanyPhone,
                request.InvoiceProvince,
                request.InvoiceDistrict,
                request.InvoiceNeighborhood,
                request.InvoiceStreet,
                request.InvoiceBuildingName,
                request.InvoiceBuildingNo,
                request.InvoiceDoorNo);
            await _profilesWrite.AddAsync(row, ct);
        }
        else
        {
            row.Update(
                request.CompanyName,
                request.LegalCompanyName,
                request.TaxOffice,
                request.TaxNumber,
                request.CompanyPhone,
                request.InvoiceProvince,
                request.InvoiceDistrict,
                request.InvoiceNeighborhood,
                request.InvoiceStreet,
                request.InvoiceBuildingName,
                request.InvoiceBuildingNo,
                request.InvoiceDoorNo);
            await _profilesWrite.UpdateAsync(row, ct);
        }

        await _profilesWrite.SaveChangesAsync(ct);

        return Result<OrganizationBillingProfileDto>.Success(new OrganizationBillingProfileDto(
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
            row.InvoiceDoorNo));
    }
}
