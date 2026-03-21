using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Pets.Queries.GetList;

public sealed record GetPetsListQuery(Guid TenantId, PageRequest PageRequest)
    : IRequest<Result<PagedResult<PetListItemDto>>>;
