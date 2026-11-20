using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Specifications;
using Backend.Veteriner.Application.VitalMeasurements.Contracts.Dtos;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.VitalMeasurements;

namespace Backend.Veteriner.Application.VitalMeasurements.Specs;

/// <summary>Eğilim penceresi: <c>FromUtc &lt;= ölçüm &lt; ToExclusiveUtc</c>; kaynak başına en yeni <c>Take</c> nokta.</summary>
public sealed record VitalSeriesWindow(
    Guid TenantId,
    Guid PetId,
    Guid? ClinicId,
    IReadOnlyCollection<Guid>? AccessibleClinicIds,
    DateTime? FromUtc,
    DateTime? ToExclusiveUtc,
    int Take);

/// <summary>İptal edilmemiş vital ölçümleri.</summary>
public sealed class VitalMeasurementSeriesSpec : Specification<VitalMeasurement, VitalSeriesPointDto>
{
    public VitalMeasurementSeriesSpec(VitalSeriesWindow w)
    {
        Query.AsNoTracking();
        Query.Where(x => x.TenantId == w.TenantId && x.PetId == w.PetId && x.CancelledAtUtc == null);
        Query.ApplyClinicScope(w.ClinicId, w.AccessibleClinicIds);
        if (w.FromUtc is { } from)
            Query.Where(x => x.MeasuredAtUtc >= from);
        if (w.ToExclusiveUtc is { } to)
            Query.Where(x => x.MeasuredAtUtc < to);
        Query.OrderByDescending(x => x.MeasuredAtUtc).ThenByDescending(x => x.Id).Take(w.Take);
        Query.Select(x => new VitalSeriesPointDto(
            VitalSeriesSources.VitalMeasurement,
            x.Id,
            x.ClinicId,
            x.MeasuredAtUtc,
            x.TemperatureC,
            x.HeartRateBpm,
            x.RespiratoryRatePerMin,
            x.WeightKg,
            x.HospitalizationId,
            x.ExaminationId));
    }
}

/// <summary>En az bir vital alanı dolu muayeneler (dolu vitalde <c>VitalsMeasuredAtUtc</c> her zaman doludur).</summary>
public sealed class ExaminationVitalSeriesSpec : Specification<Examination, VitalSeriesPointDto>
{
    public ExaminationVitalSeriesSpec(VitalSeriesWindow w)
    {
        Query.AsNoTracking();
        Query.Where(e => e.TenantId == w.TenantId && e.PetId == w.PetId && e.VitalsMeasuredAtUtc != null);
        Query.ApplyClinicScope(w.ClinicId, w.AccessibleClinicIds);
        if (w.FromUtc is { } from)
            Query.Where(e => e.VitalsMeasuredAtUtc >= from);
        if (w.ToExclusiveUtc is { } to)
            Query.Where(e => e.VitalsMeasuredAtUtc < to);
        Query.OrderByDescending(e => e.VitalsMeasuredAtUtc).ThenByDescending(e => e.Id).Take(w.Take);
        Query.Select(e => new VitalSeriesPointDto(
            VitalSeriesSources.Examination,
            e.Id,
            e.ClinicId,
            e.VitalsMeasuredAtUtc!.Value,
            e.TemperatureC,
            e.HeartRateBpm,
            e.RespiratoryRatePerMin,
            e.WeightKg,
            null,
            e.Id));
    }
}
