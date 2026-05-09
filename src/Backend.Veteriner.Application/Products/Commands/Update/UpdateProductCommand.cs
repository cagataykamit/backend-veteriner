using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Products.Commands.Update;

public sealed record UpdateProductCommand(
    Guid Id,
    Guid? ProductCategoryId,
    string Name,
    string? Sku,
    string? Barcode,
    string? Description,
    string Unit,
    decimal UnitPrice,
    string Currency)
    : IRequest<Result>;
