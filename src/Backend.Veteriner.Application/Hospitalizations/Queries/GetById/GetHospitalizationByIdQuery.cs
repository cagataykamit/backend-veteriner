using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Hospitalizations.Queries.GetById;

public sealed record GetHospitalizationByIdQuery(Guid Id)
    : IRequest<Result<HospitalizationDetailDto>>;
