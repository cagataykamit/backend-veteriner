using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.LabResults;

namespace Backend.Veteriner.Application.LabResults.Contracts.Dtos;

public sealed record LabResultDetailDto(
    Guid Id,
    Guid TenantId,
    Guid ClinicId,
    Guid PetId,
    string PetName,
    Guid ClientId,
    string ClientName,
    Guid? ExaminationId,
    DateTime? ResultDateUtc,
    string TestName,
    string? ResultText,
    string? Interpretation,
    string? Notes,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] LabResultStatus Status,
    DateTime RequestedAtUtc,
    Guid? RequestedByUserId,
    Guid? VisitId,
    bool AwaitingInterpretation,
    DateTime? CancelledAtUtc,
    Guid? CancelledByUserId,
    string? CancelReason,
    string RowVersion);
