using Ardalis.Specification;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.VitalMeasurements.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.VitalMeasurements;

namespace Backend.Veteriner.Application.VitalMeasurements.Access;

/// <summary>
/// Kimliğe göre okuma ve yazmada tek kural (IDOR): kayıt kiracıda yoksa, aktif klinik bağlamı dışındaysa ya da kullanıcı kaydın
/// kliniğine erişemiyorsa <c>VitalMeasurements.NotFound</c>; kaydın varlığı sızmaz.
/// </summary>
internal static class VitalMeasurementScope
{
    public const string NotFoundCode = "VitalMeasurements.NotFound";
    public const string NotFoundMessage = "Vital ölçümü bulunamadı.";

    public static async Task<Result<VitalMeasurement>> LoadAsync(
        IReadRepositoryBase<VitalMeasurement> measurements,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        Guid tenantId,
        Guid id,
        CancellationToken ct)
    {
        var measurement = await measurements.FirstOrDefaultAsync(new VitalMeasurementByIdSpec(tenantId, id), ct);
        if (measurement is null || (clinicContext.ClinicId is { } contextClinicId && measurement.ClinicId != contextClinicId))
            return Result<VitalMeasurement>.Failure(NotFoundCode, NotFoundMessage);

        var scope = await scopeResolver.ResolveAsync(tenantId, measurement.ClinicId, ct);
        return scope.IsSuccess
            ? Result<VitalMeasurement>.Success(measurement)
            : Result<VitalMeasurement>.Failure(NotFoundCode, NotFoundMessage);
    }
}
