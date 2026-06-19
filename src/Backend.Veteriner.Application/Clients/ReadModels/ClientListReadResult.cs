using Backend.Veteriner.Application.Clients.Contracts.Dtos;

namespace Backend.Veteriner.Application.Clients.ReadModels;

public sealed record ClientListReadResult(
    IReadOnlyList<ClientListItemDto> Items,
    int TotalCount);
