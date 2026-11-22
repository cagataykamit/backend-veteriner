using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.LabResults;

namespace Backend.Veteriner.Application.LabResults.Contracts.Dtos;

/// <summary>
/// Değer satırı ve aynı hastada aynı parametrenin (aynı birimle) bir önceki sonucu. <see cref="Flag"/> referans aralığı
/// yoksa boştur; <see cref="Previous"/> yoksa <see cref="Trend"/> da boştur.
/// </summary>
public sealed record LabResultLineDto(
    Guid Id,
    int SortOrder,
    string ParameterName,
    decimal Value,
    string? Unit,
    decimal? ReferenceLow,
    decimal? ReferenceHigh,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] LabValueFlag? Flag,
    LabResultLinePreviousDto? Previous,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] LabValueTrend? Trend);

public sealed record LabResultLinePreviousDto(
    Guid LabResultId,
    DateTime ResultDateUtc,
    decimal Value,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] LabValueFlag? Flag);
