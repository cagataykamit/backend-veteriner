using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SpeciesReference.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.SpeciesReference.Commands.Update;

public sealed class UpdateSpeciesCommandHandler : IRequestHandler<UpdateSpeciesCommand, Result>
{
    private readonly IReadRepository<Species> _speciesRead;
    private readonly IRepository<Species> _speciesWrite;

    public UpdateSpeciesCommandHandler(IReadRepository<Species> speciesRead, IRepository<Species> speciesWrite)
    {
        _speciesRead = speciesRead;
        _speciesWrite = speciesWrite;
    }

    public async Task<Result> Handle(UpdateSpeciesCommand request, CancellationToken ct)
    {
        var entity = await _speciesRead.FirstOrDefaultAsync(new SpeciesByIdSpec(request.Id), ct);
        if (entity is null)
            return Result.Failure("Species.NotFound", "Tür bulunamadı.");

        var code = request.Code.Trim().ToUpperInvariant();
        var nameLower = request.Name.Trim().ToLowerInvariant();

        var dupCode = await _speciesRead.FirstOrDefaultAsync(new SpeciesByCodeExcludingIdSpec(code, request.Id), ct);
        if (dupCode is not null)
            return Result.Failure(
                "Species.DuplicateCode",
                "Bu tür kodu zaten kullanılıyor.");

        var dupName = await _speciesRead.FirstOrDefaultAsync(new SpeciesByNameLowerExcludingIdSpec(nameLower, request.Id), ct);
        if (dupName is not null)
            return Result.Failure(
                "Species.DuplicateName",
                "Bu tür adı zaten kullanılıyor (büyük/küçük harf ayrımı yapılmaz).");

        entity.Update(request.Code, request.Name, request.DisplayOrder, request.IsActive);
        await _speciesWrite.UpdateAsync(entity, ct);
        await _speciesWrite.SaveChangesAsync(ct);
        return Result.Success();
    }
}
