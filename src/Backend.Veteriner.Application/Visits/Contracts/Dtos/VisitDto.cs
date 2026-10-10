using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Visits;

namespace Backend.Veteriner.Application.Visits.Contracts.Dtos;

public sealed record VisitDto(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    Guid? AppointmentId,
    Guid? ResponsibleVeterinarianUserId,
    string? ResponsibleVeterinarianName,
    DateTime ArrivedAtUtc,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] VisitCareStatus CareStatus,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    bool IsVoided,
    string? VoidReason,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc);
