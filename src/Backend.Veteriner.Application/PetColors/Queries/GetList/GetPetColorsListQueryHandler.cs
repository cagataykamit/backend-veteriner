using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.PetColors.Contracts.Dtos;
using Backend.Veteriner.Application.PetColors.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.PetColors.Queries.GetList;

public sealed class GetPetColorsListQueryHandler
    : IRequestHandler<GetPetColorsListQuery, Result<IReadOnlyList<PetColorListItemDto>>>
{
    private readonly IReadRepository<PetColor> _colors;

    public GetPetColorsListQueryHandler(IReadRepository<PetColor> colors) => _colors = colors;

    public async Task<Result<IReadOnlyList<PetColorListItemDto>>> Handle(
        GetPetColorsListQuery request,
        CancellationToken ct)
    {
        var rows = await _colors.ListAsync(new PetColorsActiveOrderedSpec(), ct);
        var items = rows
            .Select(c => new PetColorListItemDto(c.Id, c.Code, c.Name, c.IsActive, c.DisplayOrder))
            .ToList();

        return Result<IReadOnlyList<PetColorListItemDto>>.Success(items);
    }
}
