using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Specifications;
using Backend.Veteriner.Application.VitalMeasurements.Contracts.Dtos;
using Backend.Veteriner.Domain.VitalMeasurements;

namespace Backend.Veteriner.Application.VitalMeasurements.Specs;

/// <summary>Hasta listesi süzgeci (sayım ve sayfa aynı koşulu kullanır).</summary>
public sealed record VitalMeasurementListFilter(
    Guid TenantId,
    Guid PetId,
    Guid? ClinicId,
    IReadOnlyCollection<Guid>? AccessibleClinicIds,
    Guid? HospitalizationId,
    Guid? ExaminationId,
    DateTime? DateFromUtc,
    DateTime? DateToUtc,
    bool IncludeCancelled);

internal static class VitalMeasurementListFilterExtensions
{
    public static void Apply(this ISpecificationBuilder<VitalMeasurement> query, VitalMeasurementListFilter f)
    {
        query.AsNoTracking();
        query.Where(x => x.TenantId == f.TenantId && x.PetId == f.PetId);
        // İptal edilen ölçüm (ADR-043 K4) varsayılan gizli.
        if (!f.IncludeCancelled)
            query.Where(x => x.CancelledAtUtc == null);
        query.ApplyClinicScope(f.ClinicId, f.AccessibleClinicIds);
        if (f.HospitalizationId is { } hospitalizationId)
            query.Where(x => x.HospitalizationId == hospitalizationId);
        if (f.ExaminationId is { } examinationId)
            query.Where(x => x.ExaminationId == examinationId);
        if (f.DateFromUtc is { } from)
            query.Where(x => x.MeasuredAtUtc >= from);
        if (f.DateToUtc is { } to)
            query.Where(x => x.MeasuredAtUtc <= to);
    }
}

public sealed class VitalMeasurementsCountSpec : Specification<VitalMeasurement>
{
    public VitalMeasurementsCountSpec(VitalMeasurementListFilter filter) => Query.Apply(filter);
}

/// <summary>En yeni üstte: <c>MeasuredAtUtc</c> azalan, sonra <c>Id</c> azalan.</summary>
public sealed class VitalMeasurementsPagedSpec : Specification<VitalMeasurement, VitalMeasurementListItemDto>
{
    public VitalMeasurementsPagedSpec(VitalMeasurementListFilter filter, int page, int pageSize)
    {
        Query.Apply(filter);
        Query.OrderByDescending(x => x.MeasuredAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
        Query.Select(x => new VitalMeasurementListItemDto(
            x.Id,
            x.ClinicId,
            x.PetId,
            x.HospitalizationId,
            x.ExaminationId,
            x.MeasuredAtUtc,
            x.TemperatureC,
            x.HeartRateBpm,
            x.RespiratoryRatePerMin,
            x.WeightKg,
            x.Note,
            x.CancelledAtUtc != null,
            x.AddedAfterFinalization));
    }
}
