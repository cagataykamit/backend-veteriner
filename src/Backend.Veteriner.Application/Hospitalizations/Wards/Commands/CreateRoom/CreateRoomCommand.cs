using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Commands.CreateRoom;

/// <summary>Bölüme yeni oda/kafes; aynı bölümün aktif odaları arasında ad benzersizdir. Pasif bölüme de oda eklenebilir.</summary>
public sealed record CreateRoomCommand(Guid ClinicId, Guid WardId, string Name, int SortOrder) : IRequest<Result<Guid>>;
