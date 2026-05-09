using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Products.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Products.Queries.GetList;

public sealed record GetProductsListQuery(
    PageRequest PageRequest,
    Guid? ProductCategoryId = null,
    bool? IsActive = null)
    : IRequest<Result<PagedResult<ProductDto>>>;
