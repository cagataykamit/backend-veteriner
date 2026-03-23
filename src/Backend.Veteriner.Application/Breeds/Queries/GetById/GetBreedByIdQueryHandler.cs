using Backend.Veteriner.Application.BreedsReference.Contracts.Dtos;
using Backend.Veteriner.Application.BreedsReference.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.BreedsReference.Queries.GetById;

public sealed class GetBreedByIdQueryHandler : IRequestHandler<GetBreedByIdQuery, Result<BreedDetailDto>>
{
    private readonly IReadRepository<Breed> _breeds;

    public GetBreedByIdQueryHandler(IReadRepository<Breed> breeds) => _breeds = breeds;

    public async Task<Result<BreedDetailDto>> Handle(GetBreedByIdQuery request, CancellationToken ct)
    {
        var entity = await _breeds.FirstOrDefaultAsync(new BreedByIdWithSpeciesSpec(request.Id), ct);
        if (entity is null)
            return Result<BreedDetailDto>.Failure("Breeds.NotFound", "Irk bulunamadı.");

        if (entity.Species is null)
            return Result<BreedDetailDto>.Failure("Breeds.Inconsistent", "Irk kaydına tür bilgisi bağlanamadı.");

        var dto = new BreedDetailDto(
            entity.Id,
            entity.SpeciesId,
            entity.Species.Code,
            entity.Species.Name,
            entity.Name,
            entity.IsActive);
        return Result<BreedDetailDto>.Success(dto);
    }
}
