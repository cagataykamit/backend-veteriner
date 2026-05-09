namespace Backend.Veteriner.Application.ProductCategories.Contracts.Dtos;

public sealed record ProductCategoryDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive);
