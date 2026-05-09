using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.ProductCategories.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ProductCategories.Queries.GetList;

public sealed record GetProductCategoriesListQuery(
    PageRequest PageRequest,
    bool? IsActive = null)
    : IRequest<Result<PagedResult<ProductCategoryDto>>>;
