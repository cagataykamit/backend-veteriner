using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Payments.Queries.GetById;

public sealed record GetPaymentByIdQuery(Guid Id) : IRequest<Result<PaymentDetailDto>>;
