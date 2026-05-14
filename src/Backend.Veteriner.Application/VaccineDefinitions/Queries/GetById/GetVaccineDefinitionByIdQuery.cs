using Backend.Veteriner.Application.VaccineDefinitions.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.VaccineDefinitions.Queries.GetById;

public sealed record GetVaccineDefinitionByIdQuery(Guid Id) : IRequest<Result<VaccineDefinitionDto>>;
