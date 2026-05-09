using Backend.Veteriner.Application.ProductCategories.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ProductCategories.Commands.Create;

public sealed record CreateProductCategoryCommand(
    string Name,
    string? Description = null)
    : IRequest<Result<ProductCategoryDto>>;
