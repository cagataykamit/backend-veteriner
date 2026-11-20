using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.VitalMeasurements.Contracts.Dtos;
using Backend.Veteriner.Application.VitalMeasurements.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.VitalMeasurements;
using Mediator;

namespace Backend.Veteriner.Application.VitalMeasurements.Queries.GetList;

/// <summary>
/// Hasta kartı klinikler arasıdır: klinik verilmezse kullanıcının erişebildiği klinikler (zaman çizelgesi ile aynı kapsam kuralı).
/// </summary>
public sealed class GetVitalMeasurementsListQueryHandler
    : IRequestHandler<GetVitalMeasurementsListQuery, Result<PagedResult<VitalMeasurementListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<VitalMeasurement> _measurements;

    public GetVitalMeasurementsListQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<VitalMeasurement> measurements)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _measurements = measurements;
    }

    public async ValueTask<Result<PagedResult<VitalMeasurementListItemDto>>> Handle(
        GetVitalMeasurementsListQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<VitalMeasurementListItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (ClinicContextRule.Conflicts(request.ClinicId, _clinicContext.ClinicId))
            return Result<PagedResult<VitalMeasurementListItemDto>>.Failure(ClinicContextRule.Mismatch("VitalMeasurements"));

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);

        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, request.ClinicId ?? _clinicContext.ClinicId, ct);
        if (!scope.IsSuccess)
            return Result<PagedResult<VitalMeasurementListItemDto>>.Failure(scope.Error);

        var filter = new VitalMeasurementListFilter(
            tenantId,
            request.PetId,
            scope.Value!.SingleClinicId,
            scope.Value.AccessibleClinicIds,
            request.HospitalizationId,
            request.ExaminationId,
            request.DateFromUtc,
            request.DateToUtc,
            request.IncludeCancelled);

        var total = await _measurements.CountAsync(new VitalMeasurementsCountSpec(filter), ct);
        var items = total == 0
            ? []
            : await _measurements.ListAsync(new VitalMeasurementsPagedSpec(filter, page, pageSize), ct);

        return Result<PagedResult<VitalMeasurementListItemDto>>.Success(
            PagedResult<VitalMeasurementListItemDto>.Create(items, total, page, pageSize));
    }
}
