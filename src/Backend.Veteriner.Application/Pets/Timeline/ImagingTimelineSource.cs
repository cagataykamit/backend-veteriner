using System.Linq.Expressions;
using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imaging;
using Backend.Veteriner.Application.Pets.Access;
using Backend.Veteriner.Domain.Imaging;

namespace Backend.Veteriner.Application.Pets.Timeline;

/// <summary>
/// Görüntüleme kaydı (ADR-007, IMG-008): <c>PerformedAtUtc</c>'te olay; başlık türün adı, özet açıklama, küçük resim yok.
/// İptal edilenler varsayılan gizlidir; kesinleşmiş muayeneye sonradan bağlanan kayıt rozetlidir.
/// </summary>
internal sealed class ImagingTimelineSource(IReadRepository<ImagingRecord> repository)
    : PetTimelineSourceBase<ImagingRecord>(repository)
{
    public override PetRecordKind Kind => PetRecordKind.Imaging;

    protected override Expression<Func<ImagingRecord, DateTime>> EventAt => r => r.PerformedAtUtc;

    protected override Expression<Func<ImagingRecord, Guid>> Id => r => r.Id;

    protected override Expression<Func<ImagingRecord, bool>> Filter(PetTimelineSourceQuery q)
        => q.IncludeVoided
            ? r => r.TenantId == q.TenantId && r.PetId == q.PetId
            : r => r.TenantId == q.TenantId && r.PetId == q.PetId && r.Status == ImagingRecordStatus.Active;

    protected override ISpecificationBuilder<ImagingRecord> ApplyClinicScope(
        ISpecificationBuilder<ImagingRecord> builder,
        PetTimelineSourceQuery q)
    {
        if (q.SingleClinicId is { } clinicId)
            return builder.Where(r => r.ClinicId == clinicId);
        if (q.AccessibleClinicIds is not { } accessible)
            return builder;
        return accessible.Count == 0
            ? builder.Where(_ => false)
            : builder.Where(r => accessible.Contains(r.ClinicId));
    }

    protected override PetTimelineRow ToRow(ImagingRecord r)
        => new(
            Kind,
            r.Id,
            r.PerformedAtUtc,
            r.ClinicId,
            ImagingModalityLabels.Of(r.Modality),
            r.Description,
            r.Status.ToString(),
            r.Status == ImagingRecordStatus.Cancelled,
            RelatedExaminationId: r.ExaminationId,
            RelatedVisitId: null,
            RelatedAppointmentId: null,
            UpdatedAtUtc: r.UpdatedAtUtc,
            AddedAfterFinalization: r.AddedAfterExaminationFinalization);
}
