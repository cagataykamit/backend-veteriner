using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Commands.Create;

public sealed record CreateClinicCommand(string Name, string City)
    : IRequest<Result<Guid>>;
