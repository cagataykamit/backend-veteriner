using System.Globalization;
using System.Linq.Expressions;
using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Specifications;
using Backend.Veteriner.Application.Pets.Access;
using Backend.Veteriner.Domain.VitalMeasurements;

namespace Backend.Veteriner.Application.Pets.Timeline;

/// <summary>
/// Vital ölçümü (HOSP-004): <c>MeasuredAtUtc</c>'te olay; başlık sabit, özet yalnız değerlerin kısa metni (not timeline'a konmaz).
/// İptal edilenler varsayılan gizlidir; kesinleşmiş muayeneye sonradan bağlanan ölçüm rozetlidir.
/// </summary>
internal sealed class VitalMeasurementTimelineSource(IReadRepository<VitalMeasurement> repository)
    : PetTimelineSourceBase<VitalMeasurement>(repository)
{
    public const string Title = "Vital ölçümü";

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public override PetRecordKind Kind => PetRecordKind.VitalMeasurement;

    protected override Expression<Func<VitalMeasurement, DateTime>> EventAt => m => m.MeasuredAtUtc;

    protected override Expression<Func<VitalMeasurement, Guid>> Id => m => m.Id;

    protected override Expression<Func<VitalMeasurement, bool>> Filter(PetTimelineSourceQuery q)
        => q.IncludeVoided
            ? m => m.TenantId == q.TenantId && m.PetId == q.PetId
            : m => m.TenantId == q.TenantId && m.PetId == q.PetId && m.CancelledAtUtc == null;

    protected override ISpecificationBuilder<VitalMeasurement> ApplyClinicScope(
        ISpecificationBuilder<VitalMeasurement> builder,
        PetTimelineSourceQuery q)
        => builder.ApplyClinicScope(q.SingleClinicId, q.AccessibleClinicIds);

    protected override PetTimelineRow ToRow(VitalMeasurement m)
        => new(
            Kind,
            m.Id,
            m.MeasuredAtUtc,
            m.ClinicId,
            Title,
            Summarize(m),
            Status: m.IsCancelled ? "Cancelled" : null,
            IsVoided: m.IsCancelled,
            RelatedExaminationId: m.ExaminationId,
            RelatedVisitId: null,
            RelatedAppointmentId: null,
            UpdatedAtUtc: m.UpdatedAtUtc,
            AddedAfterFinalization: m.AddedAfterFinalization);

    /// <summary>Ör. "38,6 °C · Nabız 120/dk · Solunum 24/dk · 12,45 kg"; yalnız dolu değerler.</summary>
    internal static string Summarize(VitalMeasurement m)
    {
        var parts = new List<string>(4);
        if (m.TemperatureC is { } temperature)
            parts.Add(string.Create(Turkish, $"{temperature:0.0} °C"));
        if (m.HeartRateBpm is { } heartRate)
            parts.Add(string.Create(Turkish, $"Nabız {heartRate}/dk"));
        if (m.RespiratoryRatePerMin is { } respiratoryRate)
            parts.Add(string.Create(Turkish, $"Solunum {respiratoryRate}/dk"));
        if (m.WeightKg is { } weight)
            parts.Add(string.Create(Turkish, $"{weight:0.##} kg"));
        return string.Join(" · ", parts);
    }
}
