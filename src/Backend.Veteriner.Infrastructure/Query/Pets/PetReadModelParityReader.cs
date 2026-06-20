using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Query.Pets;

/// <summary>
/// Command DB <c>Pets</c> ile Query DB <c>PetReadModels</c> satır sayısını okuyup parity üretir.
/// Salt okunur (<c>AsNoTracking</c>); yazma/projection davranışına dokunmaz.
/// </summary>
public sealed class PetReadModelParityReader : IPetReadModelParityReader
{
    private readonly AppDbContext _commandDb;
    private readonly QueryDbContext _queryDb;

    public PetReadModelParityReader(AppDbContext commandDb, QueryDbContext queryDb)
    {
        _commandDb = commandDb;
        _queryDb = queryDb;
    }

    public async Task<PetReadModelParityResult> GetGlobalParityAsync(
        CancellationToken cancellationToken = default)
    {
        var commandCount = await _commandDb.Pets.AsNoTracking().LongCountAsync(cancellationToken);
        var queryCount = await _queryDb.PetReadModels.AsNoTracking().LongCountAsync(cancellationToken);

        return PetReadModelParityEvaluator.Evaluate(commandCount, queryCount);
    }

    public async Task<PetReadModelParityResult> GetTenantParityAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var commandCount = await _commandDb.Pets.AsNoTracking()
            .LongCountAsync(p => p.TenantId == tenantId, cancellationToken);
        var queryCount = await _queryDb.PetReadModels.AsNoTracking()
            .LongCountAsync(x => x.TenantId == tenantId, cancellationToken);

        return PetReadModelParityEvaluator.Evaluate(commandCount, queryCount, tenantId);
    }
}
