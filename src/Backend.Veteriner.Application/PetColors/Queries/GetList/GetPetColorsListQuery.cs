using Backend.Veteriner.Application.PetColors.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.PetColors.Queries.GetList;

public sealed record GetPetColorsListQuery : IRequest<Result<IReadOnlyList<PetColorListItemDto>>>;
