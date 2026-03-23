using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Pets.Queries.GetById;

public sealed class GetPetByIdQueryHandler : IRequestHandler<GetPetByIdQuery, Result<PetDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Pet> _pets;

    public GetPetByIdQueryHandler(ITenantContext tenantContext, IReadRepository<Pet> pets)
    {
        _tenantContext = tenantContext;
        _pets = pets;
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

        var dto = new PetDetailDto(
            pet.Id,
            pet.TenantId,
            pet.ClientId,
            pet.Name,
            pet.SpeciesId,
            pet.Species.Name,
            pet.Breed,
            pet.BirthDate);
        return Result<PetDetailDto>.Success(dto);
    }
}
