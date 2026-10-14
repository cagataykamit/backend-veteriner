using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Imports.Queries.GetList;

public sealed class GetImportBatchesListQueryHandler
    : IRequestHandler<GetImportBatchesListQuery, Result<PagedResult<ImportBatchDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<ImportBatch> _batches;

    public GetImportBatchesListQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<ImportBatch> batches)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _batches = batches;
    }

    public async Task<Result<PagedResult<ImportBatchDto>>> Handle(GetImportBatchesListQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<ImportBatchDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        // Aktif klinik bağlamı varsa yalnız o klinik (ve klinik bağsız partiler); yoksa kullanıcının erişim kapsamı.
        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, _clinicContext.ClinicId, ct);
        if (!scope.IsSuccess)
            return Result<PagedResult<ImportBatchDto>>.Failure(scope.Error);

        var singleClinicId = scope.Value!.SingleClinicId;
        var accessibleClinicIds = scope.Value!.AccessibleClinicIds;

        var total = await _batches.CountAsync(
            new ImportBatchesFilteredSpec(tenantId, request.Status, singleClinicId, accessibleClinicIds), ct);
        var rows = await _batches.ListAsync(
            new ImportBatchesFilteredSpec(
                tenantId, request.Status, singleClinicId, accessibleClinicIds, request.Page, request.PageSize), ct);

        return Result<PagedResult<ImportBatchDto>>.Success(
            PagedResult<ImportBatchDto>.Create(rows.Select(r => r.ToDto()).ToList(), total, request.Page, request.PageSize));
    }
}
