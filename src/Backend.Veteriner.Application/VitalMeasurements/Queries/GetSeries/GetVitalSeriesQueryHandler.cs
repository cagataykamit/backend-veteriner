using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hospitalizations.Specs;
using Backend.Veteriner.Application.Pets.Access;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.VitalMeasurements.Contracts.Dtos;
using Backend.Veteriner.Application.VitalMeasurements.Specs;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.VitalMeasurements;
using Mediator;

namespace Backend.Veteriner.Application.VitalMeasurements.Queries.GetSeries;

/// <summary>
/// İki kaynağı birleştirir (VITAL_SIGNS_API_CONTRACT.md §6): iptal edilmemiş vital ölçümleri ve vitali dolu muayeneler. Muayene kaynağı
/// <c>Examinations.Read</c> ister; yoksa atlanır ve <c>OmittedSources</c>'ta döner. Sınırdan fazla nokta varsa en yeniler kalır.
/// </summary>
public sealed class GetVitalSeriesQueryHandler : IRequestHandler<GetVitalSeriesQuery, Result<VitalSeriesDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly ICurrentUserPermissionChecker _permissionChecker;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Hospitalization> _hospitalizations;
    private readonly IReadRepository<VitalMeasurement> _measurements;
    private readonly IReadRepository<Examination> _examinations;

    public GetVitalSeriesQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        ICurrentUserPermissionChecker permissionChecker,
        IReadRepository<Pet> pets,
        IReadRepository<Hospitalization> hospitalizations,
        IReadRepository<VitalMeasurement> measurements,
        IReadRepository<Examination> examinations)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _permissionChecker = permissionChecker;
        _pets = pets;
        _hospitalizations = hospitalizations;
        _measurements = measurements;
        _examinations = examinations;
    }

    public async ValueTask<Result<VitalSeriesDto>> Handle(GetVitalSeriesQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Fail("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        if (ClinicContextRule.Conflicts(request.ClinicId, _clinicContext.ClinicId))
            return Result<VitalSeriesDto>.Failure(ClinicContextRule.Mismatch("VitalMeasurements"));

        if (await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, request.PetId), ct) is null)
            return Fail("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, request.ClinicId ?? _clinicContext.ClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<VitalSeriesDto>.Failure(scopeResult.Error);

        var scope = scopeResult.Value!;
        var clinicId = scope.SingleClinicId;
        var accessibleClinicIds = scope.AccessibleClinicIds;
        var fromUtc = request.FromUtc.HasValue ? ToUtc(request.FromUtc.Value) : (DateTime?)null;
        var toExclusiveUtc = request.ToUtc.HasValue ? ToUtc(request.ToUtc.Value) : (DateTime?)null;

        if (request.HospitalizationId is { } hospitalizationId)
        {
            var h = await _hospitalizations.FirstOrDefaultAsync(new HospitalizationByIdSpec(tenantId, hospitalizationId), ct);
            if (h is null || h.PetId != request.PetId || !scope.Includes(h.ClinicId))
                return Fail("Hospitalizations.NotFound", "Yatış kaydı bulunamadı.");

            // Yatış süresi (Hospitalization.CoversInstant): kabul ve taburcu dahil; üst uç yarı açık pencereye bir tik eklenerek çevrilir.
            clinicId = h.ClinicId;
            accessibleClinicIds = null;
            fromUtc = Max(fromUtc, h.AdmittedAtUtc);
            if (h.DischargedAtUtc is { } dischargedAtUtc)
                toExclusiveUtc = Min(toExclusiveUtc, dischargedAtUtc.AddTicks(1));
        }

        var canReadExaminations = _permissionChecker.CanRead(PetRecordKind.Examination);
        IReadOnlyList<string> omitted = canReadExaminations ? [] : [VitalSeriesSources.Examination];
        if (accessibleClinicIds is { Count: 0 } || (fromUtc.HasValue && toExclusiveUtc.HasValue && fromUtc >= toExclusiveUtc))
            return Result<VitalSeriesDto>.Success(new VitalSeriesDto([], false, omitted));

        var limit = request.Limit ?? GetVitalSeriesQuery.DefaultLimit;
        var window = new VitalSeriesWindow(tenantId, request.PetId, clinicId, accessibleClinicIds, fromUtc, toExclusiveUtc, limit + 1);
        var points = new List<VitalSeriesPointDto>(await _measurements.ListAsync(new VitalMeasurementSeriesSpec(window), ct));
        if (canReadExaminations)
            points.AddRange(await _examinations.ListAsync(new ExaminationVitalSeriesSpec(window), ct));

        var newest = points
            .OrderByDescending(p => p.MeasuredAtUtc)
            .ThenByDescending(p => p.Source, StringComparer.Ordinal)
            .ThenByDescending(p => p.SourceId)
            .ToList();
        var truncated = newest.Count > limit;
        IReadOnlyList<VitalSeriesPointDto> ordered = newest.Take(limit).Reverse().ToList();

        return Result<VitalSeriesDto>.Success(new VitalSeriesDto(ordered, truncated, omitted));
    }

    private static DateTime? Max(DateTime? value, DateTime bound) => value is { } v && v > bound ? v : bound;

    private static DateTime? Min(DateTime? value, DateTime bound) => value is { } v && v < bound ? v : bound;

    private static DateTime ToUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    private static Result<VitalSeriesDto> Fail(string code, string message) => Result<VitalSeriesDto>.Failure(code, message);
}
