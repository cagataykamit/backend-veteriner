using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Pets.Queries.GetHistorySummary;

public sealed record GetPetHistorySummaryQuery(Guid PetId)
    : IRequest<Result<PetHistorySummaryDto>>;
