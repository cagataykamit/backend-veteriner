using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Pets.Commands.Create;

public sealed class CreatePetCommandHandler : IRequestHandler<CreatePetCommand, Result<Guid>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _petsRead;
    private readonly IRepository<Pet> _petsWrite;

    public CreatePetCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<Tenant> tenants,
        IReadRepository<Client> clients,
        IReadRepository<Pet> petsRead,
        IRepository<Pet> petsWrite)
    {
        _tenantContext = tenantContext;
        _tenants = tenants;
        _clients = clients;
        _petsRead = petsRead;
        _petsWrite = petsWrite;
    }

    public async Task<Result<Guid>> Handle(CreatePetCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<Guid>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<Guid>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
            return Result<Guid>.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için hayvan kaydı oluşturulamaz.");

        var client = await _clients.FirstOrDefaultAsync(
            new ClientByIdSpec(tenantId, request.ClientId), ct);
        if (client is null)
            return Result<Guid>.Failure("Clients.NotFound", "Müşteri bulunamadı veya kiracıya ait değil.");

        if (request.BirthDate.HasValue
            && request.BirthDate.Value > DateOnly.FromDateTime(DateTime.UtcNow))
            return Result<Guid>.Failure("Pets.BirthDateInFuture", "Doğum tarihi gelecekte olamaz.");

        var nameKey = request.Name.Trim().ToLowerInvariant();
        var speciesKey = request.Species.Trim().ToLowerInvariant();
        var duplicate = await _petsRead.FirstOrDefaultAsync(
            new PetByClientNameAndSpeciesCaseInsensitiveSpec(request.ClientId, nameKey, speciesKey), ct);
        if (duplicate is not null)
            return Result<Guid>.Failure(
                "Pets.DuplicatePet",
                "Bu müşteri için aynı isim ve türde bir hayvan kaydı zaten var.");

        var pet = new Pet(
            tenantId,
            request.ClientId,
            request.Name,
            request.Species,
            request.Breed,
            request.BirthDate);
        await _petsWrite.AddAsync(pet, ct);
        await _petsWrite.SaveChangesAsync(ct);
        return Result<Guid>.Success(pet.Id);
    }
}
