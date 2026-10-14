using Backend.Veteriner.Application.Sales.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Sales.Queries.GetById;

public sealed record GetSaleByIdQuery(Guid Id) : IRequest<Result<SaleDto>>;
