using Backend.Veteriner.Application.Products.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Products.Queries.GetById;

public sealed record GetProductByIdQuery(Guid Id) : IRequest<Result<ProductDto>>;
