using Backend.Veteriner.Application.SpeciesReference.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.SpeciesReference.Queries.GetById;

public sealed record GetSpeciesByIdQuery(Guid Id) : IRequest<Result<SpeciesDetailDto>>;
