using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Pets.Queries.GetById;

public sealed class GetPetByIdQueryHandler : IRequestHandler<GetPetByIdQuery, Result<PetDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;

    public GetPetByIdQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients)
    {
        _tenantContext = tenantContext;
        _pets = pets;
        _clients = clients;
    }

    public async Task<Result<PetDetailDto>> Handle(GetPetByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PetDetailDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, request.Id), ct);
        if (pet is null)
            return Result<PetDetailDto>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı.");

        if (pet.Species is null)
            return Result<PetDetailDto>.Failure("Pets.Inconsistent", "Hayvan tür bilgisi yüklenemedi.");

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, pet.ClientId), ct);
        var clientName = client?.FullName ?? string.Empty;
        var clientPhone = client?.Phone;
        var clientEmail = client?.Email;

        var dto = new PetDetailDto(
            pet.Id,
            pet.TenantId,
            pet.ClientId,
            clientName,
            clientPhone,
            clientEmail,
            pet.Name,
            pet.SpeciesId,
            pet.Species.Name,
            pet.ColorId,
            pet.ColorRef?.Name,
            pet.Breed,
            pet.BirthDate,
            pet.BreedId,
            pet.Gender,
            pet.Weight,
            pet.Notes);
        return Result<PetDetailDto>.Success(dto);
    }
}
