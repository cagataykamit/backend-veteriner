using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Examinations;

namespace Backend.Veteriner.Application.Examinations.Contracts.Dtos;

public sealed record ExaminationListItemDto(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    string PetName,
    Guid ClientId,
    string ClientName,
    Guid? AppointmentId,
    DateTime ExaminedAtUtc,
    string VisitReason,
    Guid? VisitId = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ExaminationStatus Status = ExaminationStatus.Draft);
