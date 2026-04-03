using Backend.Veteriner.Application.LabResults.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.LabResults.Queries.GetById;

public sealed record GetLabResultByIdQuery(Guid Id)
    : IRequest<Result<LabResultDetailDto>>;
