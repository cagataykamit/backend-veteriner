using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Commands.Create;

public sealed record CreateClinicCommand(
    string Name,
    string City,
    string? Phone = null,
    string? Email = null,
    string? Address = null,
    string? Description = null)
    : IRequest<Result<Guid>>;
