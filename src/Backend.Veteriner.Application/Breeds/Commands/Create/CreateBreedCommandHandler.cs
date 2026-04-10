using Backend.Veteriner.Application.BreedsReference.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SpeciesReference.Specs;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.BreedsReference.Commands.Create;

public sealed class CreateBreedCommandHandler : IRequestHandler<CreateBreedCommand, Result<Guid>>
{
    private readonly ITenantContext _tenantContext;
    private readonly TenantSubscriptionEffectiveWriteEvaluator _writeEvaluator;
    private readonly IReadRepository<Species> _speciesRead;
    private readonly IReadRepository<Breed> _breedsRead;
    private readonly IRepository<Breed> _breedsWrite;

    public CreateBreedCommandHandler(
        ITenantContext tenantContext,
        TenantSubscriptionEffectiveWriteEvaluator writeEvaluator,
        IReadRepository<Species> speciesRead,
        IReadRepository<Breed> breedsRead,
        IRepository<Breed> breedsWrite)
    {
        _tenantContext = tenantContext;
        _writeEvaluator = writeEvaluator;
        _speciesRead = speciesRead;
        _breedsRead = breedsRead;
        _breedsWrite = breedsWrite;
    }

    public async Task<Result<Guid>> Handle(CreateBreedCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<Guid>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var writeGate = await _writeEvaluator.EnsureWriteAllowedAsync(tenantId, ct);
        if (!writeGate.IsSuccess)
            return Result<Guid>.Failure(writeGate.Error);

        var species = await _speciesRead.FirstOrDefaultAsync(new SpeciesByIdSpec(request.SpeciesId), ct);
        if (species is null)
            return Result<Guid>.Failure("Breeds.SpeciesNotFound", "Tür bulunamadı; ırk oluşturulamaz.");

        var nameLower = request.Name.Trim().ToLowerInvariant();
        var dup = await _breedsRead.FirstOrDefaultAsync(
            new BreedBySpeciesAndNameLowerSpec(request.SpeciesId, nameLower), ct);
        if (dup is not null)
            return Result<Guid>.Failure(
                "Breeds.DuplicateName",
                "Bu tür altında aynı ada sahip bir ırk zaten var.");

        var entity = new Breed(request.SpeciesId, request.Name);
        await _breedsWrite.AddAsync(entity, ct);
        await _breedsWrite.SaveChangesAsync(ct);
        return Result<Guid>.Success(entity.Id);
    }
}
