using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ProductCategories.Commands.Update;

public sealed record UpdateProductCategoryCommand(
    Guid Id,
    string Name,
    string? Description = null)
    : IRequest<Result>;
