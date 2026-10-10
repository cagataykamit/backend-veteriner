using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Examinations;

namespace Backend.Veteriner.Application.Examinations.Contracts.Dtos;

/// <summary>Başarılı kesinleştirme sonrası güncel sürüm ve kesinleştirme bilgisi.</summary>
public sealed record ExaminationFinalizeResultDto(
    Guid Id,
    string RowVersion,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ExaminationStatus Status,
    DateTime FinalizedAtUtc,
    Guid FinalizedByUserId);
