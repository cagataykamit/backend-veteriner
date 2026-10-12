namespace Backend.Veteriner.Application.Prescriptions.Contracts.Dtos;

public sealed record PrescriptionItemDto(
    Guid Id,
    int SortOrder,
    string Name,
    Guid? ProductId,
    string? Instructions,
    decimal? Quantity);
