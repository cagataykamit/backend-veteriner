using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Examinations.Access;
using Backend.Veteriner.Application.Hospitalizations.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.VitalMeasurements.Access;
using Backend.Veteriner.Application.VitalMeasurements.Contracts.Dtos;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.VitalMeasurements;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.VitalMeasurements.Commands.Update;

/// <summary>
/// Ölçümü günceller. Bağlı muayene kesinleşmişse reddedilir (<c>Examinations.Finalized</c>; geç kayıt olarak eklenmiş ölçüm dahil,
/// düzeltme gerekçeli iptal + yeni kayıt). Yatışa bağlı ölçümün yeni zamanı yatış süresi içinde olmalıdır.
/// </summary>
public sealed class UpdateVitalMeasurementCommandHandler
    : IRequestHandler<UpdateVitalMeasurementCommand, Result<VitalMeasurementWriteResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Hospitalization> _hospitalizations;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IRepository<VitalMeasurement> _measurements;
    private readonly TimeProvider _timeProvider;

    public UpdateVitalMeasurementCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Hospitalization> hospitalizations,
        IReadRepository<Examination> examinations,
        IRepository<VitalMeasurement> measurements,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _hospitalizations = hospitalizations;
        _examinations = examinations;
        _measurements = measurements;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<VitalMeasurementWriteResultDto>> Handle(UpdateVitalMeasurementCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Fail("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Fail("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
            return Fail("Tenants.TenantInactive", "Pasif kiracı için vital ölçümü güncellenemez.");

        var loaded = await VitalMeasurementScope.LoadAsync(_measurements, _clinicContext, _clinicScopeResolver, tenantId, request.Id, ct);
        if (!loaded.IsSuccess)
            return Result<VitalMeasurementWriteResultDto>.Failure(loaded.Error);

        var measurement = loaded.Value!;
        if (!ExaminationRowVersion.TryDecode(request.RowVersion, out var expectedRowVersion))
            return Fail(VitalMeasurement.ValidationCode, VitalMeasurementConcurrency.RowVersionInvalidMessage);

        var values = VitalSigns.Create(request.TemperatureC, request.HeartRateBpm, request.RespiratoryRatePerMin, request.WeightKg);
        if (!values.IsSuccess)
            return Result<VitalMeasurementWriteResultDto>.Failure(values.Error);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var measuredAtUtc = VitalMeasuredAtWindow.ToUtc(request.MeasuredAtUtc);
        var window = VitalMeasuredAtWindow.Validate(measuredAtUtc, utcNow);
        if (!window.IsSuccess)
            return Result<VitalMeasurementWriteResultDto>.Failure(window.Error);

        var locked = await ExaminationLinkedRecordLock.EnsureOpenAsync(_examinations, tenantId, [measurement.ExaminationId], ct);
        if (!locked.IsSuccess)
            return Result<VitalMeasurementWriteResultDto>.Failure(locked.Error);

        if (measurement.HospitalizationId is { } hospitalizationId)
        {
            var hospitalization = await _hospitalizations.FirstOrDefaultAsync(
                new HospitalizationByIdSpec(tenantId, hospitalizationId), ct);
            if (hospitalization is not null)
            {
                var within = VitalMeasurementLinkRules.EnsureWithinHospitalization(hospitalization, measuredAtUtc);
                if (!within.IsSuccess)
                    return Result<VitalMeasurementWriteResultDto>.Failure(within.Error);
            }
        }

        var updated = measurement.Update(measuredAtUtc, values.Value!, request.Note, utcNow);
        if (!updated.IsSuccess)
            return Result<VitalMeasurementWriteResultDto>.Failure(updated.Error);

        measurement.SetExpectedRowVersion(expectedRowVersion);

        try
        {
            await _measurements.UpdateAsync(measurement, ct);
            await _measurements.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail(VitalMeasurementConcurrency.ConflictCode, VitalMeasurementConcurrency.ConflictMessage);
        }

        return Result<VitalMeasurementWriteResultDto>.Success(
            new VitalMeasurementWriteResultDto(measurement.Id, ExaminationRowVersion.Encode(measurement.RowVersion)));
    }

    private static Result<VitalMeasurementWriteResultDto> Fail(string code, string message)
        => Result<VitalMeasurementWriteResultDto>.Failure(code, message);
}
