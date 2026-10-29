using Backend.Veteriner.Application.Pets.Access;

namespace Backend.Veteriner.Application.Pets.Timeline;

/// <summary>Kaynak okuyucunun ürettiği ortak satır; klinik adı ve bağlantı handler'da tamamlanır.</summary>
public sealed record PetTimelineRow(
    PetRecordKind Kind,
    Guid SourceId,
    DateTime EventAtUtc,
    Guid ClinicId,
    string Title,
    string? Summary,
    string? Status,
    bool IsVoided,
    Guid? RelatedExaminationId,
    Guid? RelatedVisitId,
    Guid? RelatedAppointmentId,
    DateTime? UpdatedAtUtc,
    bool AddedAfterFinalization = false,
    Guid? RelatedTreatmentId = null);

/// <param name="Take">Kaynak başına en çok satır (sayfa boyutu + 1).</param>
public sealed record PetTimelineSourceQuery(
    Guid TenantId,
    Guid PetId,
    Guid? SingleClinicId,
    IReadOnlyCollection<Guid>? AccessibleClinicIds,
    DateTime? FromUtc,
    DateTime? ToUtc,
    bool IncludeVoided,
    PetTimelineCursor? Cursor,
    int Take);

/// <summary>Timeline'a bir kayıt türü bağlar; yeni tür (ör. ödeme) yalnızca yeni bir okuyucu ekler.</summary>
public interface IPetTimelineSource
{
    PetRecordKind Kind { get; }

    /// <summary>Sorguya uyan satırlar, <c>(eventAtUtc DESC, sourceId DESC)</c> sıralı.</summary>
    Task<IReadOnlyList<PetTimelineRow>> ReadAsync(PetTimelineSourceQuery query, CancellationToken ct);
}
