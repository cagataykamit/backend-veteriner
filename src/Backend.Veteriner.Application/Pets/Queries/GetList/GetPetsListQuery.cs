using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Pets.Queries.GetList;

/// <param name="Status">Verilmezse tüm durumlar (önceki davranış).</param>
public sealed record GetPetsListQuery(
    PageRequest PageRequest,
    Guid? ClientId = null,
    Guid? SpeciesId = null,
    PetStatus? Status = null)
    : IRequest<Result<PagedResult<PetListItemDto>>>;
