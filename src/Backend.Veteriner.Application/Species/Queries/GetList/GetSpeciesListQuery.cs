using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.SpeciesReference.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.SpeciesReference.Queries.GetList;

public sealed record GetSpeciesListQuery(PageRequest PageRequest, bool? IsActive)
    : IRequest<Result<PagedResult<SpeciesListItemDto>>>;
