namespace Backend.Veteriner.Application.Clients.ReadModels;

public sealed record ClientNamesLookupRequest(
    Guid TenantId,
    IReadOnlyCollection<Guid> ClientIds);
