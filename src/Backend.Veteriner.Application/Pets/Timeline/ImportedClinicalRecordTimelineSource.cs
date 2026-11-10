using System.Linq.Expressions;
using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Access;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Pets.Timeline;

/// <summary>
/// Eski program kaydı (ADR-048 T1, IMPORT_API_CONTRACT.md §12.8): salt okunur içe aktarılmış klinik geçmiş. Olay anı kaydın
/// gününün İstanbul 12:00'ıdır (UTC 09:00; Türkiye 2016'dan beri yaz saati uygulamıyor). Başlık kayıt türü, özet tanı
/// (yoksa şikâyet) ilk 200 karakter; iptal kavramı yoktur; bağlantı yoktur (ayrıntı hasta ekranındaki listeden).
/// </summary>
internal sealed class ImportedClinicalRecordTimelineSource(IReadRepository<ImportedClinicalRecord> repository)
    : PetTimelineSourceBase<ImportedClinicalRecord>(repository)
{
    public const string DefaultTitle = "Eski program kaydı";
    private const int SummaryLength = 200;
    private static readonly TimeOnly IstanbulNoonUtc = new(9, 0);

    public override PetRecordKind Kind => PetRecordKind.ImportedClinicalRecord;

    protected override Expression<Func<ImportedClinicalRecord, DateTime>> EventAt
        => r => r.RecordedOn.ToDateTime(IstanbulNoonUtc);

    protected override Expression<Func<ImportedClinicalRecord, Guid>> Id => r => r.Id;

    protected override Expression<Func<ImportedClinicalRecord, bool>> Filter(PetTimelineSourceQuery q)
        => r => r.TenantId == q.TenantId && r.PetId == q.PetId;

    protected override ISpecificationBuilder<ImportedClinicalRecord> ApplyClinicScope(
        ISpecificationBuilder<ImportedClinicalRecord> builder,
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

    protected override PetTimelineRow ToRow(ImportedClinicalRecord r)
    {
        var summary = r.Diagnosis ?? r.Complaint;
        return new(
            Kind,
            r.Id,
            DateTime.SpecifyKind(r.RecordedOn.ToDateTime(IstanbulNoonUtc), DateTimeKind.Utc),
            r.ClinicId,
            r.RecordType ?? DefaultTitle,
            summary is { Length: > SummaryLength } ? summary[..SummaryLength] : summary,
            null,
            false,
            RelatedExaminationId: null,
            RelatedVisitId: null,
            RelatedAppointmentId: null,
            UpdatedAtUtc: null,
            AddedAfterFinalization: false);
    }
}
