using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.LabResults;

namespace Backend.Veteriner.Application.LabResults.Contracts.Dtos;

/// <summary>Bir parametrenin hastadaki geçmiş değerlerinden biri (eğilim listesi; yeniden eskiye).</summary>
public sealed record LabParameterHistoryPointDto(
    Guid LabResultId,
    DateTime ResultDateUtc,
    string TestName,
    string ParameterName,
    decimal Value,
    string? Unit,
    decimal? ReferenceLow,
    decimal? ReferenceHigh,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] LabValueFlag? Flag);
