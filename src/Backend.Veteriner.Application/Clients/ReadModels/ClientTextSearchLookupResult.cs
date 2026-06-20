namespace Backend.Veteriner.Application.Clients.ReadModels;

public sealed record ClientTextSearchLookupResult(IReadOnlyList<Guid> ClientIds);
