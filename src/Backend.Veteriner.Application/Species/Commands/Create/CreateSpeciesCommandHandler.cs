using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SpeciesReference.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.SpeciesReference.Commands.Create;

public sealed class CreateSpeciesCommandHandler : IRequestHandler<CreateSpeciesCommand, Result<Guid>>
{
    private readonly IReadRepository<Species> _speciesRead;
    private readonly IRepository<Species> _speciesWrite;

    public CreateSpeciesCommandHandler(IReadRepository<Species> speciesRead, IRepository<Species> speciesWrite)
    {
        _speciesRead = speciesRead;
        _speciesWrite = speciesWrite;
    }

    public async Task<Result<Guid>> Handle(CreateSpeciesCommand request, CancellationToken ct)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var nameLower = request.Name.Trim().ToLowerInvariant();

        var dupCode = await _speciesRead.FirstOrDefaultAsync(new SpeciesByCodeSpec(code), ct);
        if (dupCode is not null)
            return Result<Guid>.Failure(
                "Species.DuplicateCode",
                "Bu tür kodu zaten kullanılıyor.");

        var dupName = await _speciesRead.FirstOrDefaultAsync(new SpeciesByNameLowerSpec(nameLower), ct);
        if (dupName is not null)
            return Result<Guid>.Failure(
                "Species.DuplicateName",
                "Bu tür adı zaten kullanılıyor (büyük/küçük harf ayrımı yapılmaz).");

        var entity = new Species(request.Code, request.Name, request.DisplayOrder);
        await _speciesWrite.AddAsync(entity, ct);
        await _speciesWrite.SaveChangesAsync(ct);
        return Result<Guid>.Success(entity.Id);
    }
}
