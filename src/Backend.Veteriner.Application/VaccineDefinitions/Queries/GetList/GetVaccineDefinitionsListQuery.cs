using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.VaccineDefinitions.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.VaccineDefinitions.Queries.GetList;

public sealed record GetVaccineDefinitionsListQuery(
    PageRequest PageRequest,
    Guid? SpeciesId,
    bool IncludeInactive) : IRequest<Result<PagedResult<VaccineDefinitionDto>>>;
