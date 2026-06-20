using Backend.Veteriner.Application.BreedsReference.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.PetColors.Specs;
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
    private readonly IReadRepository<PetColor> _colorsRead;
    private readonly IReadRepository<Breed> _breedsRead;
    private readonly IReadRepository<Pet> _petsRead;
    private readonly IRepository<Pet> _petsWrite;
    private readonly IPetIntegrationEventOutbox _eventOutbox;

    public UpdatePetCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<Tenant> tenants,
        IReadRepository<Client> clients,
        IReadRepository<Species> speciesRead,
        IReadRepository<PetColor> colorsRead,
        IReadRepository<Breed> breedsRead,
        IReadRepository<Pet> petsRead,
        IRepository<Pet> petsWrite,
        IPetIntegrationEventOutbox eventOutbox)
    {
        _tenantContext = tenantContext;
        _tenants = tenants;
        _clients = clients;
        _speciesRead = speciesRead;
        _colorsRead = colorsRead;
        _breedsRead = breedsRead;
        _petsRead = petsRead;
        _petsWrite = petsWrite;
        _eventOutbox = eventOutbox;
    }

    public async Task<Result> Handle(UpdatePetCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result.Failure("Tenants.NotFound", "Tenant bulunamadı.");
        if (!tenant.IsActive)
            return Result.Failure("Tenants.TenantInactive", "Pasif kiraci icin hayvan kaydi guncellenemez.");

        var pet = await _petsRead.FirstOrDefaultAsync(new PetByIdSpec(tenantId, request.Id), ct);
        if (pet is null)
            return Result.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, request.ClientId), ct);
        if (client is null)
            return Result.Failure("Clients.NotFound", "Müşteri bulunamadı veya kiracıya ait değil.");

        var species = await _speciesRead.FirstOrDefaultAsync(new SpeciesByIdSpec(request.SpeciesId), ct);
        if (species is null || !species.IsActive)
            return Result.Failure("Pets.SpeciesNotFound", "Tür bulunamadı veya pasif; geçerli bir SpeciesId gönderin.");

        Breed? breedRef = null;
        if (request.BreedId is { } breedId)
        {
            var breed = await _breedsRead.FirstOrDefaultAsync(new BreedByIdWithSpeciesSpec(breedId), ct);
            if (breed is null || !breed.IsActive)
                return Result.Failure(
                    "Pets.BreedNotFound",
                    "Irk bulunamadı veya pasif; geçerli bir BreedId gönderin.");
            if (breed.SpeciesId != request.SpeciesId)
                return Result.Failure(
                    "Pets.BreedSpeciesMismatch",
                    "Secilen irk, secilen tur ile uyusmuyor.");
            breedRef = breed;
        }

        PetColor? colorRef = null;
        if (request.ColorId is { } colorId)
        {
            var color = await _colorsRead.FirstOrDefaultAsync(new PetColorByIdSpec(colorId), ct);
            if (color is null || !color.IsActive)
                return Result.Failure(
                    "Pets.ColorNotFound",
                    "Renk bulunamadı veya pasif; geçerli bir ColorId gönderin.");
            colorRef = color;
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
            request.Gender,
            request.ColorId,
            request.Weight,
            request.Notes);

        await _petsWrite.UpdateAsync(pet, ct);

        // Outbox emission aynı SaveChanges/transaction sınırında kalıcı olur (buffer interceptor ile drain edilir).
        await _eventOutbox.EnqueueAsync(
            PetIntegrationEventTypes.Updated,
            new PetUpdatedIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                PetProjectionSnapshotFactory.Create(pet, client, species, breedRef, colorRef)),
            ct);

        await _petsWrite.SaveChangesAsync(ct);
        return Result.Success();
    }
}