using Backend.Veteriner.Application.ProductCategories.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ProductCategories.Queries.GetById;

public sealed record GetProductCategoryByIdQuery(Guid Id) : IRequest<Result<ProductCategoryDto>>;
