using Backend.Veteriner.Application.BreedsReference.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.BreedsReference.Commands.Update;

public sealed class UpdateBreedCommandHandler : IRequestHandler<UpdateBreedCommand, Result>
{
    private readonly IReadRepository<Breed> _breedsRead;
    private readonly IRepository<Breed> _breedsWrite;
    private readonly ICatalogCacheInvalidator _catalogCache;

    public UpdateBreedCommandHandler(
        IReadRepository<Breed> breedsRead,
        IRepository<Breed> breedsWrite,
        ICatalogCacheInvalidator catalogCache)
    {
        _breedsRead = breedsRead;
        _breedsWrite = breedsWrite;
        _catalogCache = catalogCache;
    }

    public async Task<Result> Handle(UpdateBreedCommand request, CancellationToken ct)
    {
        var entity = await _breedsRead.FirstOrDefaultAsync(new BreedByIdWithSpeciesSpec(request.Id), ct);
        if (entity is null)
            return Result.Failure("Breeds.NotFound", "Irk bulunamadı.");

        var nameLower = request.Name.Trim().ToLowerInvariant();
        var dup = await _breedsRead.FirstOrDefaultAsync(
            new BreedBySpeciesAndNameLowerExcludingIdSpec(entity.SpeciesId, nameLower, request.Id), ct);
        if (dup is not null)
            return Result.Failure(
                "Breeds.DuplicateName",
                "Bu tür altında aynı ada sahip bir ırk zaten var.");

        entity.Update(request.Name, request.IsActive);
        await _breedsWrite.UpdateAsync(entity, ct);
        await _breedsWrite.SaveChangesAsync(ct);
        _catalogCache.InvalidateBreeds();
        return Result.Success();
    }
}
