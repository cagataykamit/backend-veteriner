using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.LabResults;

namespace Backend.Veteriner.Application.LabResults.Contracts.Dtos;

public sealed record LabResultListItemDto(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    string PetName,
    Guid ClientId,
    string ClientName,
    DateTime? ResultDateUtc,
    string TestName,
    Guid? ExaminationId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] LabResultStatus Status,
    DateTime RequestedAtUtc,
    bool AwaitingInterpretation);
