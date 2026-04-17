using Backend.Veteriner.Application.BreedsReference.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.BreedsReference.Queries.GetList;

/// <param name="Search">Metin araması; boş/whitespace ise uygulanmaz. Kaynak: <c>PageRequest.Search</c> / query <c>search</c>.</param>
public sealed record GetBreedListQuery(PageRequest PageRequest, bool? IsActive, Guid? SpeciesId, string? Search)
    : IRequest<Result<PagedResult<BreedListItemDto>>>;
