using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Queries.GetById;

public sealed record GetClinicByIdQuery(Guid Id) : IRequest<Result<ClinicDetailDto>>;
