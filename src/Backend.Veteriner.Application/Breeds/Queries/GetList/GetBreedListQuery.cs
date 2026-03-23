using Backend.Veteriner.Application.BreedsReference.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.BreedsReference.Queries.GetList;

public sealed record GetBreedListQuery(PageRequest PageRequest, bool? IsActive, Guid? SpeciesId)
    : IRequest<Result<PagedResult<BreedListItemDto>>>;
