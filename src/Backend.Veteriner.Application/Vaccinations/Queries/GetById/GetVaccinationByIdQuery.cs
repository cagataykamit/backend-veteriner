using Backend.Veteriner.Application.Vaccinations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Vaccinations.Queries.GetById;

public sealed record GetVaccinationByIdQuery(Guid Id) : IRequest<Result<VaccinationDetailDto>>;
