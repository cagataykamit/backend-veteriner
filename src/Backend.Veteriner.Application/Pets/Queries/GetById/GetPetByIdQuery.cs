using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Pets.Queries.GetById;

public sealed record GetPetByIdQuery(Guid Id) : IRequest<Result<PetDetailDto>>;
