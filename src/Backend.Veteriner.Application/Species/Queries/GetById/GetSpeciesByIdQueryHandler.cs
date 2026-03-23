using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SpeciesReference.Contracts.Dtos;
using Backend.Veteriner.Application.SpeciesReference.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.SpeciesReference.Queries.GetById;

public sealed class GetSpeciesByIdQueryHandler : IRequestHandler<GetSpeciesByIdQuery, Result<SpeciesDetailDto>>
{
    private readonly IReadRepository<Species> _species;

    public GetSpeciesByIdQueryHandler(IReadRepository<Species> species) => _species = species;

    public async Task<Result<SpeciesDetailDto>> Handle(GetSpeciesByIdQuery request, CancellationToken ct)
    {
        var entity = await _species.FirstOrDefaultAsync(new SpeciesByIdSpec(request.Id), ct);
        if (entity is null)
            return Result<SpeciesDetailDto>.Failure("Species.NotFound", "Tür bulunamadı.");

        var dto = new SpeciesDetailDto(entity.Id, entity.Code, entity.Name, entity.IsActive, entity.DisplayOrder);
        return Result<SpeciesDetailDto>.Success(dto);
    }
}
