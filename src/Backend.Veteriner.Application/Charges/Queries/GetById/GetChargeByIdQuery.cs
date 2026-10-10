using Backend.Veteriner.Application.Charges.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Charges.Queries.GetById;

public sealed record GetChargeByIdQuery(Guid Id) : IRequest<Result<ChargeDto>>;
