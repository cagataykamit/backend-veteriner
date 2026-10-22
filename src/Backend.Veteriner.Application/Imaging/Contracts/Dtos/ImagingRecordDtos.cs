using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Files.Contracts.Dtos;
using Backend.Veteriner.Domain.Imaging;

namespace Backend.Veteriner.Application.Imaging.Contracts.Dtos;

public sealed record ImagingRecordDetailDto(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    string PetName,
    Guid ClientId,
    string ClientName,
    Guid? ExaminationId,
    DateTime PerformedAtUtc,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ImagingModality Modality,
    string? Description,
    string? ClinicalNote,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ImagingRecordStatus Status,
    bool AddedAfterExaminationFinalization,
    DateTime CreatedAtUtc,
    Guid CreatedByUserId,
    DateTime? UpdatedAtUtc,
    DateTime? CancelledAtUtc,
    Guid? CancelledByUserId,
    string? CancelReason,
    string RowVersion,
    IReadOnlyList<StoredFileDto> Files);

public sealed record ImagingRecordListItemDto(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    string PetName,
    Guid ClientId,
    string ClientName,
    Guid? ExaminationId,
    DateTime PerformedAtUtc,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ImagingModality Modality,
    string? Description,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ImagingRecordStatus Status,
    int FileCount,
    bool AddedAfterExaminationFinalization);

public sealed record ImagingRecordWriteResultDto(Guid Id, string RowVersion);
