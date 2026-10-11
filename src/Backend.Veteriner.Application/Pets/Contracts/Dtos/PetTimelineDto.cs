using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Pets.Access;

namespace Backend.Veteriner.Application.Pets.Contracts.Dtos;

/// <summary>Kaynak kaydı ifade eder; backend URL üretmez, istemci <see cref="Type"/> → rota eşlemesi yapar.</summary>
public sealed record PetTimelineLinkRefDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetRecordKind Type,
    Guid Id);

public sealed record PetTimelineItemDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetRecordKind Type,
    Guid SourceId,
    DateTime EventAtUtc,
    Guid ClinicId,
    string ClinicName,
    string Title,
    string? Summary,
    string? Status,
    bool IsVoided,
    PetTimelineLinkRefDto LinkRef,
    Guid? RelatedExaminationId,
    Guid? RelatedVisitId,
    Guid? RelatedAppointmentId,
    DateTime? UpdatedAtUtc);

/// <param name="NextCursor">Opak; null ise sayfa sonu.</param>
/// <param name="OmittedTypes">Yetki yok nedeniyle sorguya girmeyen türler (boş sonuçtan ayırt etmek için).</param>
public sealed record PetTimelineDto(
    IReadOnlyList<PetTimelineItemDto> Items,
    string? NextCursor,
    IReadOnlyList<string> OmittedTypes);
