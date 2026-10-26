using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Commands.UpdateRoom;

/// <summary>Oda adı ve sırası; oda bölümler arası taşınmaz. Yanıt odanın güncel hali.</summary>
public sealed record UpdateRoomCommand(Guid ClinicId, Guid WardId, Guid RoomId, string Name, int SortOrder)
    : IRequest<Result<HospitalizationRoomDto>>;
