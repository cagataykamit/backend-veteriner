using Backend.Veteriner.Application.BreedsReference.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.SpeciesReference.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Pets.Commands.Update;

public sealed class UpdatePetCommandHandler : IRequestHandler<UpdatePetCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Species> _speciesRead;
    private readonly IReadRepository<Breed> _breedsRead;
    private readonly IReadRepository<Pet> _petsRead;
    private readonly IRepository<Pet> _petsWrite;

    public UpdatePetCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<Tenant> tenants,
        IReadRepository<Client> clients,
        IReadRepository<Species> speciesRead,
        IReadRepository<Breed> breedsRead,
        IReadRepository<Pet> petsRead,
        IRepository<Pet> petsWrite)
    {
        _tenantContext = tenantContext;
        _tenants = tenants;
        _clients = clients;
        _speciesRead = speciesRead;
        _breedsRead = breedsRead;
        _petsRead = petsRead;
        _petsWrite = petsWrite;
    }

    public async Task<Result> Handle(UpdatePetCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result.Failure(
                "Tenants.ContextMissing",
                "Kiraci baglami yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result.Failure("Tenants.NotFound", "Tenant bulunamadi.");
        if (!tenant.IsActive)
            return Result.Failure("Tenants.TenantInactive", "Pasif kiraci icin hayvan kaydi guncellenemez.");

        var pet = await _petsRead.FirstOrDefaultAsync(new PetByIdSpec(tenantId, request.Id), ct);
        if (pet is null)
            return Result.Failure("Pets.NotFound", "Hayvan kaydi bulunamadi veya kiraciya ait degil.");

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, request.ClientId), ct);
        if (client is null)
            return Result.Failure("Clients.NotFound", "Musteri bulunamadi veya kiraciya ait degil.");

        var species = await _speciesRead.FirstOrDefaultAsync(new SpeciesByIdSpec(request.SpeciesId), ct);
        if (species is null || !species.IsActive)
            return Result.Failure("Pets.SpeciesNotFound", "Tur bulunamadi veya pasif; gecerli bir SpeciesId gonderin.");

        if (request.BreedId is { } breedId)
        {
            var breed = await _breedsRead.FirstOrDefaultAsync(new BreedByIdWithSpeciesSpec(breedId), ct);
            if (breed is null || !breed.IsActive)
                return Result.Failure(
                    "Pets.BreedNotFound",
                    "Irk bulunamadi veya pasif; gecerli bir BreedId gonderin.");
            if (breed.SpeciesId != request.SpeciesId)
                return Result.Failure(
                    "Pets.BreedSpeciesMismatch",
                    "Secilen irk, secilen tur ile uyusmuyor.");
        }

        if (request.BirthDate.HasValue && request.BirthDate.Value > DateOnly.FromDateTime(DateTime.UtcNow))
            return Result.Failure("Pets.BirthDateInFuture", "Dogum tarihi gelecekte olamaz.");

        var nameKey = request.Name.Trim().ToLowerInvariant();
        var duplicate = await _petsRead.FirstOrDefaultAsync(
            new PetByClientNameAndSpeciesIdExcludingIdSpec(request.ClientId, nameKey, request.SpeciesId, request.Id), ct);
        if (duplicate is not null)
            return Result.Failure("Pets.DuplicatePet", "Bu musteri icin ayni isim ve turde bir hayvan kaydi zaten var.");

        pet.UpdateDetails(
            request.Name,
            request.SpeciesId,
            request.Breed,
            request.BirthDate,
            request.BreedId,
            request.Gender);

        await _petsWrite.UpdateAsync(pet, ct);
        await _petsWrite.SaveChangesAsync(ct);
        return Result.Success();
    }
}