using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.VitalMeasurements.Access;
using Backend.Veteriner.Application.VitalMeasurements.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.VitalMeasurements;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.VitalMeasurements.Commands.Cancel;

/// <summary>
/// Ölçümü gerekçeyle iptal eder (silmez). Kesinleşmiş muayene kilidi ve yatışın durumu iptali engellemez
/// (düzeltme yolu "gerekçeli iptal + yeni kayıt", ADR-013).
/// </summary>
public sealed class CancelVitalMeasurementCommandHandler
    : IRequestHandler<CancelVitalMeasurementCommand, Result<VitalMeasurementWriteResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IRepository<VitalMeasurement> _measurements;
    private readonly TimeProvider _timeProvider;

    public CancelVitalMeasurementCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IRepository<VitalMeasurement> measurements,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _measurements = measurements;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<VitalMeasurementWriteResultDto>> Handle(CancelVitalMeasurementCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Fail("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        if (_clientContext.UserId is not { } userId)
            return Fail("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Fail("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
            return Fail("Tenants.TenantInactive", "Pasif kiracı için vital ölçümü iptal edilemez.");

        var loaded = await VitalMeasurementScope.LoadAsync(_measurements, _clinicContext, _clinicScopeResolver, tenantId, request.Id, ct);
        if (!loaded.IsSuccess)
            return Result<VitalMeasurementWriteResultDto>.Failure(loaded.Error);

        var measurement = loaded.Value!;
        if (!ExaminationRowVersion.TryDecode(request.RowVersion, out var expectedRowVersion))
            return Fail(VitalMeasurement.ValidationCode, VitalMeasurementConcurrency.RowVersionInvalidMessage);

        var cancelled = measurement.Cancel(userId, request.Reason, _timeProvider.GetUtcNow().UtcDateTime);
        if (!cancelled.IsSuccess)
            return Result<VitalMeasurementWriteResultDto>.Failure(cancelled.Error);

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
