using Backend.Veteriner.Application.Prescriptions.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Prescriptions.Queries.GetById;

public sealed record GetPrescriptionByIdQuery(Guid Id)
    : IRequest<Result<PrescriptionDetailDto>>;
