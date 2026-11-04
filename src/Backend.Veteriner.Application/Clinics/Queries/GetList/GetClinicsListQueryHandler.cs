using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Clinics.Queries.GetList;

public sealed class GetClinicsListQueryHandler
    : IRequestHandler<GetClinicsListQuery, Result<PagedResult<ClinicListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicAssignmentAccessGuard _assignmentGuard;
    private readonly IUserClinicRepository _userClinics;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<ClinicTaxpayerSettings> _taxpayerSettings;

    public GetClinicsListQueryHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IClinicAssignmentAccessGuard assignmentGuard,
        IUserClinicRepository userClinics,
        IReadRepository<Clinic> clinics,
        IReadRepository<ClinicTaxpayerSettings> taxpayerSettings)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _assignmentGuard = assignmentGuard;
        _userClinics = userClinics;
        _clinics = clinics;
        _taxpayerSettings = taxpayerSettings;
    }

    public async ValueTask<Result<PagedResult<ClinicListItemDto>>> Handle(GetClinicsListQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<ClinicListItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
        {
            return Result<PagedResult<ClinicListItemDto>>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);

        IReadOnlyList<Clinic> rows;
        int total;

        if (await _assignmentGuard.MustApplyAssignedClinicScopeAsync(userId, ct))
        {
            var accessible = await _userClinics.ListAccessibleClinicsAsync(userId, tenantId, null, ct);
            total = accessible.Count;
            rows = accessible
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }
        else
        {
            total = await _clinics.CountAsync(new ClinicsByTenantCountSpec(tenantId), ct);
            rows = await _clinics.ListAsync(new ClinicsByTenantPagedSpec(tenantId, page, pageSize), ct);
        }

        var items = await rows.ToListItemDtosAsync(_taxpayerSettings, tenantId, ct);

        return Result<PagedResult<ClinicListItemDto>>.Success(
            PagedResult<ClinicListItemDto>.Create(items, total, page, pageSize));
    }
}
