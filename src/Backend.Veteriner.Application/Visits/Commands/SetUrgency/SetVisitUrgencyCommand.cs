using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Visits.Commands.SetUrgency;

/// <summary>Geliş kaydının acil işaretini koyar/kaldırır (<c>Visits.Update</c>); aynı değer idempotenttir.</summary>
public sealed record SetVisitUrgencyCommand(Guid VisitId, bool IsUrgent)
    : IRequest<Result<VisitDto>>;
