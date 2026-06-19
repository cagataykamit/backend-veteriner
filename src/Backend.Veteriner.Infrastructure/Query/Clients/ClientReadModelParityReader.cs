using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Query.Clients;

/// <summary>
/// Command DB <c>Clients</c> ile Query DB <c>ClientReadModels</c> satır sayısını okuyup parity üretir.
/// Salt okunur (<c>AsNoTracking</c>); yazma/projection davranışına dokunmaz.
/// </summary>
public sealed class ClientReadModelParityReader : IClientReadModelParityReader
{
    private readonly AppDbContext _commandDb;
    private readonly QueryDbContext _queryDb;

    public ClientReadModelParityReader(AppDbContext commandDb, QueryDbContext queryDb)
    {
        _commandDb = commandDb;
        _queryDb = queryDb;
    }

    public async Task<ClientReadModelParityResult> GetGlobalParityAsync(
        CancellationToken cancellationToken = default)
    {
        var commandCount = await _commandDb.Clients.AsNoTracking().LongCountAsync(cancellationToken);
        var queryCount = await _queryDb.ClientReadModels.AsNoTracking().LongCountAsync(cancellationToken);

        return ClientReadModelParityEvaluator.Evaluate(commandCount, queryCount);
    }

    public async Task<ClientReadModelParityResult> GetTenantParityAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var commandCount = await _commandDb.Clients.AsNoTracking()
            .LongCountAsync(c => c.TenantId == tenantId, cancellationToken);
        var queryCount = await _queryDb.ClientReadModels.AsNoTracking()
            .LongCountAsync(x => x.TenantId == tenantId, cancellationToken);

        return ClientReadModelParityEvaluator.Evaluate(commandCount, queryCount, tenantId);
    }
}
