using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Visits.Queries.GetById;

public sealed record GetVisitByIdQuery(Guid Id) : IRequest<Result<VisitDto>>;
