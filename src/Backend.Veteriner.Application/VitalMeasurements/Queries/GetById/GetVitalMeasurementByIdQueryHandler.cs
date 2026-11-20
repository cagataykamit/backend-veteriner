using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.VitalMeasurements.Access;
using Backend.Veteriner.Application.VitalMeasurements.Contracts.Dtos;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.VitalMeasurements;
using Mediator;

namespace Backend.Veteriner.Application.VitalMeasurements.Queries.GetById;

/// <summary>İptal edilmiş ölçüm de döner; kapsam dışı kayıt 404 (<see cref="VitalMeasurementScope"/>).</summary>
public sealed class GetVitalMeasurementByIdQueryHandler
    : IRequestHandler<GetVitalMeasurementByIdQuery, Result<VitalMeasurementDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<VitalMeasurement> _measurements;
    private readonly IReadRepository<Pet> _pets;

    public GetVitalMeasurementByIdQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<VitalMeasurement> measurements,
        IReadRepository<Pet> pets)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _measurements = measurements;
        _pets = pets;
    }

    public async ValueTask<Result<VitalMeasurementDetailDto>> Handle(GetVitalMeasurementByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<VitalMeasurementDetailDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var loaded = await VitalMeasurementScope.LoadAsync(_measurements, _clinicContext, _clinicScopeResolver, tenantId, request.Id, ct);
        if (!loaded.IsSuccess)
            return Result<VitalMeasurementDetailDto>.Failure(loaded.Error);

        var m = loaded.Value!;
        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, m.PetId), ct);

        return Result<VitalMeasurementDetailDto>.Success(new VitalMeasurementDetailDto(
            m.Id,
            m.TenantId,
            m.ClinicId,
            m.PetId,
            pet?.Name ?? string.Empty,
            m.HospitalizationId,
            m.ExaminationId,
            m.MeasuredAtUtc,
            m.TemperatureC,
            m.HeartRateBpm,
            m.RespiratoryRatePerMin,
            m.WeightKg,
            m.Note,
            m.CreatedByUserId,
            m.CreatedAtUtc,
            m.UpdatedAtUtc,
            ExaminationRowVersion.Encode(m.RowVersion),
            m.CancelledAtUtc,
            m.CancelReason,
            m.AddedAfterFinalization,
            m.LateEntryReason));
    }
}
