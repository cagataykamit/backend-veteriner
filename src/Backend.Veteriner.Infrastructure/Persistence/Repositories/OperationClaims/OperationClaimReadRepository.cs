using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.OperationClaims;

public sealed class OperationClaimReadRepository : IOperationClaimReadRepository
{
    private readonly AppDbContext _db;

    public OperationClaimReadRepository(AppDbContext db) => _db = db;

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct)
        => _db.OperationClaims.AnyAsync(x => x.Id == id, ct);
}
