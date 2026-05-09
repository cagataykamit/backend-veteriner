using Backend.Veteriner.Application.Products.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Products.Commands.Create;

public sealed record CreateProductCommand(
    Guid? ProductCategoryId,
    string Name,
    string? Sku,
    string? Barcode,
    string? Description,
    string Unit,
    decimal UnitPrice,
    string Currency)
    : IRequest<Result<ProductDto>>;
