using Backend.Veteriner.Application.BreedsReference.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.BreedsReference.Queries.GetById;

public sealed record GetBreedByIdQuery(Guid Id) : IRequest<Result<BreedDetailDto>>;
