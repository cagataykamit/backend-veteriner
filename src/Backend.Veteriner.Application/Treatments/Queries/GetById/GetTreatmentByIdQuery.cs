using Backend.Veteriner.Application.Treatments.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Treatments.Queries.GetById;

public sealed record GetTreatmentByIdQuery(Guid Id) : IRequest<Result<TreatmentDetailDto>>;
