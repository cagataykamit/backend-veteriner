using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Pets.Queries.GetById;

public sealed class GetPetByIdQueryHandler : IRequestHandler<GetPetByIdQuery, Result<PetDetailDto>>
{
    private readonly IReadRepository<Pet> _pets;

    public GetPetByIdQueryHandler(IReadRepository<Pet> pets) => _pets = pets;

    public async Task<Result<PetDetailDto>> Handle(GetPetByIdQuery request, CancellationToken ct)
    {
        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(request.TenantId, request.Id), ct);
        if (pet is null)
            return Result<PetDetailDto>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı.");

        var dto = new PetDetailDto(
            pet.Id,
            pet.TenantId,
            pet.ClientId,
            pet.Name,
            pet.Species,
            pet.Breed,
            pet.BirthDate);
        return Result<PetDetailDto>.Success(dto);
    }
}
