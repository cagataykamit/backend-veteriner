using Backend.Veteriner.Application.CareInstructions.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.CareInstructions.Queries.GetById;

public sealed record GetCareInstructionByIdQuery(Guid Id) : IRequest<Result<CareInstructionDto>>;
