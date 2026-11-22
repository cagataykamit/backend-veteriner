using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Appointments.Commands.Update;

public sealed record UpdateAppointmentCommand(
    Guid Id,
    Guid? ClinicId,
    Guid PetId,
    DateTime ScheduledAtUtc,
    AppointmentType AppointmentType,
    AppointmentStatus Status,
    string? Notes = null,
    int? DurationMinutes = null,
    Guid? ReasonId = null,
    Guid? VeterinarianUserId = null,
    Guid? RoomResourceId = null,
    Guid? EquipmentResourceId = null) : IRequest<Result>, ITransactionalRequest
{
    [JsonIgnore]
    public AppointmentResourceIds Resources => new(RoomResourceId, EquipmentResourceId);
}