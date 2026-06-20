namespace Backend.Veteriner.Application.Clients.ReadModels;

public sealed record ClientNamesLookupResult(IReadOnlyList<ClientNameLookupItem> Items);
