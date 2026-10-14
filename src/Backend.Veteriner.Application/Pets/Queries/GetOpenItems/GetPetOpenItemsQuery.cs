using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Pets.Queries.GetOpenItems;

/// <param name="ClinicId">Verilirse aktif klinik bağlamını geçersiz kılar.</param>
public sealed record GetPetOpenItemsQuery(Guid PetId, Guid? ClinicId = null)
    : IRequest<Result<PetOpenItemsDto>>;
